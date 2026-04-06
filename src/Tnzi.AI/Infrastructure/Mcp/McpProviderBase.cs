
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP Provider 共享基类 — 封装 FindServer、单服务器操作和遍历所有服务器的通用模式。
/// 子类只需实现具体的 SDK 调用。
/// </summary>
public abstract class McpProviderBase<TSelf> where TSelf : class
{
    protected readonly IOptions<AIOptions> Options;
    protected readonly IMcpClientFactory ClientFactory;
    protected readonly ILogger<TSelf> Logger;

    protected McpProviderBase(
        IOptions<AIOptions> options,
        IMcpClientFactory clientFactory,
        ILogger<TSelf> logger)
    {
        Options = Check.NotNull(options);
        ClientFactory = Check.NotNull(clientFactory);
        Logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 从配置中查找指定名称的服务器。MCP 未启用或服务器不存在时返回 null。
    /// </summary>
    protected McpServerConfig? FindServer(string serverName)
    {
        var mcp = Options.Value.Mcp;
        if (mcp == null || !mcp.Enabled || mcp.Servers == null || mcp.Servers.Count == 0)
        {
            return null;
        }

        return mcp.Servers.FirstOrDefault(s =>
            string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 对指定服务器执行操作，服务器不存在或异常时返回默认值。
    /// </summary>
    protected async Task<TResult> ExecuteForServerAsync<TResult>(
        string serverName,
        Func<IMcpClientAdapter, CancellationToken, Task<TResult>> operation,
        TResult defaultValue,
        string operationName,
        CancellationToken ct = default)
    {
        var server = FindServer(serverName);
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
    /// 对所有已配置的 MCP 服务器执行操作并聚合结果。单个服务器失败时跳过。
    /// </summary>
    protected async Task<IReadOnlyList<TItem>> ExecuteForAllServersAsync<TItem>(
        Func<IMcpClientAdapter, CancellationToken, Task<IReadOnlyList<TItem>>> operation,
        string operationName,
        CancellationToken ct = default)
    {
        var mcp = Options.Value.Mcp;
        if (mcp == null || !mcp.Enabled || mcp.Servers == null || mcp.Servers.Count == 0)
        {
            return Array.Empty<TItem>();
        }

        var tasks = mcp.Servers
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
