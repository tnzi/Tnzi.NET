
namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// Deferred 工具上下文提供器 — 将可用的 deferred 工具名称列表注入系统消息，
/// 引导模型通过 tool_search 工具发现和加载延迟加载的工具。
/// </summary>
/// <remarks>
/// <para>
/// 仅在 ToolDeferredRegistry.Current 存在且包含 deferred 条目时注入。
/// 注入内容使用 <c>&lt;available-deferred-tools&gt;</c> XML 标签包裹，
/// 与 SystemPromptTemplateBuilder.Tags.DeferredTools 对应。
/// </para>
/// </remarks>
public sealed class DeferredToolContextProvider : IContextProvider
{
    private readonly ILogger<DeferredToolContextProvider> _logger;

    /// <summary>
    /// 排序：在 Skills(100) 之后注入，确保技能工具先就位
    /// </summary>
    public int Order => ContextProviderOrders.Skills + 10;

    public string Name => nameof(DeferredToolContextProvider);

    public DeferredToolContextProvider(ILogger<DeferredToolContextProvider> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        var registry = ToolDeferredRegistry.Current;
        if (registry is null || registry.Count == 0)
        {
            return Task.FromResult(ContextInjection.Empty);
        }

        var deferredEntries = registry.GetDeferredEntries();
        if (deferredEntries.Count == 0)
        {
            return Task.FromResult(ContextInjection.Empty);
        }

        var content = BuildDeferredToolsSection(deferredEntries, registry.Count);

        var injection = new ContextInjection
        {
            Messages =
            [
                new ChatMessage(ChatRole.System, content)
            ]
        };

        _logger.LogDebug("Injected deferred tools section with {Count} deferred tools", deferredEntries.Count);
        return Task.FromResult(injection);
    }

    /// <summary>
    /// 构建 deferred tools section 内容
    /// </summary>
    public static string BuildDeferredToolsSection(IReadOnlyList<DeferredToolEntry> deferredEntries, int totalCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<available-deferred-tools>");
        sb.AppendLine("The following tools are available but not yet loaded. Use the `tool_search` tool to search and load them by name or keyword.");
        sb.AppendLine();

        // 列出 deferred 工具名称和简要描述
        foreach (var entry in deferredEntries)
        {
            sb.Append("- **").Append(entry.Name).Append("**");
            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                sb.Append(": ").Append(entry.Description);
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"Total available: {totalCount} tools ({deferredEntries.Count} deferred, use `tool_search` to discover and load).");
        sb.AppendLine();
        sb.AppendLine("Usage examples:");
        sb.AppendLine("- `tool_search(query: \"select:tool_name\")` — load a specific tool by exact name");
        sb.AppendLine("- `tool_search(query: \"keyword\")` — search by keyword");
        sb.AppendLine("- `tool_search(query: \"+prefix rest\")` — search by name prefix");
        sb.Append("</available-deferred-tools>");

        return sb.ToString();
    }
}
