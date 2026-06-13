
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP Provider 共享基类 — 封装按名查找服务器、单服务器操作和遍历所有服务器的通用模式。
/// 服务器枚举走 <see cref="IMcpServerCatalog"/>（部署配置 + DB 注册表合并）。
/// 子类只需实现具体的 SDK 调用。
/// </summary>
public abstract class McpProviderBase<TSelf> where TSelf : class
{
    protected readonly IMcpServerCatalog ServerCatalog;
    protected readonly IMcpClientFactory ClientFactory;
    protected readonly ILogger<TSelf> Logger;

    protected McpProviderBase(
        IMcpServerCatalog serverCatalog,
        IMcpClientFactory clientFactory,
        ILogger<TSelf> logger)
    {
        ServerCatalog = Check.NotNull(serverCatalog);
        ClientFactory = Check.NotNull(clientFactory);
        Logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 对指定服务器执行操作，服务器不存在（或 MCP 未启用）/异常时返回默认值。
    /// </summary>
    protected async Task<TResult> ExecuteForServerAsync<TResult>(
        string serverName,
        Func<IMcpClientAdapter, CancellationToken, Task<TResult>> operation,
        TResult defaultValue,
        string operationName,
        CancellationToken ct = default)
    {
        var server = await ServerCatalog.FindServerAsync(serverName, ct).ConfigureAwait(false);
        if (server == null)
        {
            return defaultValue;
        }

        try
        {
            var adapter = await ClientFactory.GetOrCreateClientAsync(server, ct).ConfigureAwait(false);
            return await operation(adapter, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to {Operation} from MCP server '{ServerName}'", operationName, serverName);
            return defaultValue;
        }
    }

    /// <summary>
    /// 对所有有效 MCP 服务器执行操作并聚合结果。单个服务器失败时跳过。
    /// </summary>
    protected async Task<IReadOnlyList<TItem>> ExecuteForAllServersAsync<TItem>(
        Func<IMcpClientAdapter, CancellationToken, Task<IReadOnlyList<TItem>>> operation,
        string operationName,
        CancellationToken ct = default)
    {
        var servers = await ServerCatalog.GetEffectiveServersAsync(ct).ConfigureAwait(false);
        if (servers.Count == 0)
        {
            return Array.Empty<TItem>();
        }

        var tasks = servers
            .Where(server => !string.IsNullOrWhiteSpace(server.Name))
            .Select(async server =>
            {
                try
                {
                    var adapter = await ClientFactory.GetOrCreateClientAsync(server, ct).ConfigureAwait(false);
                    return await operation(adapter, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to {Operation} from MCP server '{ServerName}'. Skipping.", operationName, server.Name);
                    return Array.Empty<TItem>();
                }
            });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.SelectMany(r => r).ToList();
    }
}
