
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP Prompt 模板发现与获取实现 — 通过 IMcpClientFactory 获取 MCP 客户端，调用 prompts/list 和 prompts/get。
/// 单个服务器不可用时记录警告并跳过，不阻塞整体。
/// </summary>
public class McpPromptProvider : McpProviderBase<McpPromptProvider>, IMcpPromptProvider
{
    public McpPromptProvider(
        IMcpServerCatalog serverCatalog,
        IMcpClientFactory clientFactory,
        ILogger<McpPromptProvider> logger)
        : base(serverCatalog, clientFactory, logger)
    {
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(string serverName, CancellationToken ct = default)
    {
        return ExecuteForServerAsync(
            serverName,
            static (adapter, token) => adapter.ListPromptsAsync(token),
            (IReadOnlyList<McpPromptInfo>)Array.Empty<McpPromptInfo>(),
            "list prompts",
            ct);
    }

    /// <inheritdoc />
    public Task<McpPromptResult?> GetPromptAsync(string serverName, string promptName, Dictionary<string, string>? arguments = null, CancellationToken ct = default)
    {
        return ExecuteForServerAsync(
            serverName,
            (adapter, token) => adapter.GetPromptAsync(promptName, arguments, token),
            (McpPromptResult?)null,
            "get prompt",
            ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<McpPromptInfo>> ListAllPromptsAsync(CancellationToken ct = default)
    {
        return ExecuteForAllServersAsync(
            static (adapter, token) => adapter.ListPromptsAsync(token),
            "list prompts",
            ct);
    }
}
