namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// <see cref="IUserMcpConnections"/> 的实现：同时丢掉连接工厂里的连接（含失败冷却、待刷新标记）与工具提供者里的工具缓存。
/// 只动被点名的那个用户，不波及别的用户的同名服务器。
/// </summary>
public sealed class UserMcpConnections : IUserMcpConnections
{
    private readonly IMcpClientFactory _clientFactory;
    private readonly IMcpToolProvider _toolProvider;

    public UserMcpConnections(IMcpClientFactory clientFactory, IMcpToolProvider toolProvider)
    {
        _clientFactory = Check.NotNull(clientFactory);
        _toolProvider = Check.NotNull(toolProvider);
    }

    /// <inheritdoc />
    public Task DisconnectAsync(Guid userId, string serverName, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(serverName);
        return DisconnectCoreAsync(userId, serverName, ct);
    }

    /// <inheritdoc />
    public Task DisconnectAllAsync(Guid userId, CancellationToken ct = default) => DisconnectCoreAsync(userId, null, ct);

    private async Task DisconnectCoreAsync(Guid userId, string? serverName, CancellationToken ct)
    {
        // 先丢工具缓存再断连接：反过来的话，两步之间进来的运行会命中缓存里绑在已释放连接上的工具
        if (_toolProvider is McpToolProvider provider)
        {
            provider.InvalidateUserCache(userId, serverName);
        }

        await _clientFactory.InvalidateUserClientsAsync(userId, serverName, ct).ConfigureAwait(false);
    }
}
