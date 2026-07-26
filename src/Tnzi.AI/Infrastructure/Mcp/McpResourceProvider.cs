
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 资源发现与读取实现 - 通过 IMcpClientFactory 获取 MCP 客户端，调用 resources/list 和 resources/read。
/// 单个服务器不可用时记录警告并跳过，不阻塞整体。
/// </summary>
public class McpResourceProvider : McpProviderBase<McpResourceProvider>, IMcpResourceProvider
{
    public McpResourceProvider(
        IMcpServerCatalog serverCatalog,
        IMcpClientFactory clientFactory,
        ILogger<McpResourceProvider> logger)
        : base(serverCatalog, clientFactory, logger)
    {
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(string serverName, CancellationToken ct = default)
    {
        return ExecuteForServerAsync(
            serverName,
            static (adapter, token) => adapter.ListResourcesAsync(token),
            (IReadOnlyList<McpResourceInfo>)Array.Empty<McpResourceInfo>(),
            "list resources",
            ct);
    }

    /// <inheritdoc />
    public Task<McpResourceContent?> ReadResourceAsync(string serverName, string uri, CancellationToken ct = default)
    {
        return ExecuteForServerAsync(
            serverName,
            (adapter, token) => adapter.ReadResourceAsync(uri, token),
            (McpResourceContent?)null,
            "read resource",
            ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<McpResourceInfo>> ListAllResourcesAsync(CancellationToken ct = default)
    {
        return ExecuteForAllServersAsync(
            static (adapter, token) => adapter.ListResourcesAsync(token),
            "list resources",
            ct);
    }
}
