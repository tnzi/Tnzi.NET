// 文件级：该命名空间的 Result / Prompt / Role 等类型与框架类型同名，不能放进全局 using
using ModelContextProtocol.Protocol;

namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// <see cref="McpClientAdapter"/> 的资源/提示相关 partial - Resource/Prompt 的列举与读取。
/// </summary>
/// <remarks>
/// <para>
/// 直接调用 SDK 的强类型 API。这四个方法返回 <c>ValueTask&lt;T&gt;</c>：此前经反射调用、只认 <c>Task</c> 的实现
/// 把未等待的 ValueTask 本身当结果对象解析，对任何服务器都安静地返回空列表 / null（请求发出去了却没人等它）。
/// </para>
/// <para>
/// 服务器没有声明对应能力（<c>resources</c> / <c>prompts</c>）时不发请求、直接返回空。
/// 其余异常照常抛出，由 <see cref="McpProviderBase{TSelf}"/> 按服务器记录警告并跳过。
/// </para>
/// </remarks>
internal sealed partial class McpClientAdapter
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(CancellationToken ct = default)
    {
        if (!SupportsResources)
        {
            return Array.Empty<McpResourceInfo>();
        }

        var resources = await _client.ListResourcesAsync(cancellationToken: ct).ConfigureAwait(false);
        return resources
            .Where(r => !string.IsNullOrWhiteSpace(r.Uri))
            .Select(r => new McpResourceInfo(r.Uri, r.Name, r.MimeType, r.Description))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<McpResourceContent?> ReadResourceAsync(string uri, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(uri);
        if (!SupportsResources)
        {
            return null;
        }

        var result = await _client.ReadResourceAsync(uri, cancellationToken: ct).ConfigureAwait(false);

        // 一次读取可能返回多段内容：取第一段能表示成文本的（与契约「一个资源一段内容」一致）
        foreach (var contents in result.Contents)
        {
            switch (contents)
            {
                case TextResourceContents text:
                    return new McpResourceContent(text.Text, text.MimeType);
                case BlobResourceContents blob:
                    return new McpResourceContent(BlobToBase64(blob), blob.MimeType);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(CancellationToken ct = default)
    {
        if (!SupportsPrompts)
        {
            return Array.Empty<McpPromptInfo>();
        }

        var prompts = await _client.ListPromptsAsync(cancellationToken: ct).ConfigureAwait(false);
        return prompts
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new McpPromptInfo(p.Name, p.Description, ToPromptArguments(p.ProtocolPrompt.Arguments)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<McpPromptResult?> GetPromptAsync(string promptName, Dictionary<string, string>? arguments = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(promptName);
        if (!SupportsPrompts)
        {
            return null;
        }

        // SDK 的参数类型是 IReadOnlyDictionary<string, object?>：Dictionary<string, string> 不能直接传，逐项转换
        var promptArguments = arguments?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);
        var result = await _client.GetPromptAsync(promptName, promptArguments, cancellationToken: ct).ConfigureAwait(false);

        var messages = new List<McpPromptMessage>();
        foreach (var message in result.Messages)
        {
            var text = ToText(message.Content);
            if (text == null)
            {
                _logger.LogDebug(
                    "MCP server '{ServerName}' prompt '{PromptName}' returned a {ContentType} message that has no text form; skipped",
                    _serverName, promptName, message.Content?.GetType().Name);
                continue;
            }

            messages.Add(new McpPromptMessage(message.Role == Role.Assistant ? "assistant" : "user", text));
        }

        return messages.Count > 0 ? new McpPromptResult(messages) : null;
    }

    private bool SupportsResources => _client.ServerCapabilities?.Resources != null;

    private bool SupportsPrompts => _client.ServerCapabilities?.Prompts != null;

    private static IReadOnlyList<McpPromptArgument>? ToPromptArguments(IList<PromptArgument>? arguments)
    {
        if (arguments is not { Count: > 0 })
        {
            return null;
        }

        return arguments
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new McpPromptArgument(a.Name, a.Description, a.Required == true))
            .ToList();
    }

    private static string? ToText(ContentBlock? content) => content switch
    {
        TextContentBlock text => text.Text,
        EmbeddedResourceBlock { Resource: TextResourceContents resource } => resource.Text,
        _ => null
    };

    private static string BlobToBase64(BlobResourceContents blob) => Convert.ToBase64String(blob.DecodedData.Span);
}
