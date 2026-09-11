namespace Tnzi.SignalR.Services;

/// <summary>
/// <see cref="IHubConnectionAborter"/> 的进程内实现。
///
/// 表里存的是活连接自己的 <see cref="HubCallerContext"/>，它本来就活着，
/// 所以这张表不额外延长任何对象的寿命；条目由 <c>TnziHub.OnDisconnectedAsync</c>
/// 移除，而 <see cref="Abort"/> 触发的断开同样会走到那里。
/// </summary>
public class HubConnectionAborter : IHubConnectionAborter
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _connections = new(StringComparer.Ordinal);
    private readonly ILogger<HubConnectionAborter> _logger;

    /// <summary>
    /// 初始化一个<see cref="HubConnectionAborter"/>类型的新实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public HubConnectionAborter(ILogger<HubConnectionAborter> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public int RegisteredCount => _connections.Count;

    /// <inheritdoc />
    public void Register(string connectionId, HubCallerContext context)
    {
        Check.NotNullOrWhiteSpace(connectionId);
        Check.NotNull(context);
        _connections[connectionId] = context;
    }

    /// <inheritdoc />
    public void Unregister(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        _connections.TryRemove(connectionId, out _);
    }

    /// <inheritdoc />
    public bool Abort(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return false;
        if (!_connections.TryRemove(connectionId, out var context)) return false;

        try
        {
            context.Abort();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // 连接已经自行结束；结果与我们想要的一致，但不算"我们断的"
            return false;
        }
    }

    /// <inheritdoc />
    public int AbortRange(IEnumerable<string> connectionIds)
    {
        Check.NotNull(connectionIds);

        var aborted = 0;
        foreach (var connectionId in connectionIds)
        {
            if (Abort(connectionId)) aborted++;
        }

        if (aborted > 0)
        {
            _logger.LogInformation("Aborted {Count} realtime connection(s)", aborted);
        }

        return aborted;
    }
}
