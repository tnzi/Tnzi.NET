namespace Tnzi.AI.Channels.Adapters;

/// <summary>
/// 通用 access-token 缓存刷新器。
/// 使用双重检查锁（SemaphoreSlim）保证线程安全，到期前 <paramref name="earlyRenewSeconds"/> 秒触发预刷新。
/// </summary>
public sealed class TokenRefresher : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> _refreshFunc;
    private readonly ILogger _logger;
    private readonly string _adapterName;
    private readonly int _earlyRenewSeconds;

    private string? _token;
    private DateTime _expiry = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <param name="refreshFunc">执行实际 token 刷新的委托，返回 (token, 有效秒数)</param>
    /// <param name="logger">用于记录 debug 日志</param>
    /// <param name="adapterName">适配器名称（用于日志）</param>
    /// <param name="earlyRenewSeconds">到期前提前刷新的秒数（默认 300）</param>
    public TokenRefresher(
        Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> refreshFunc,
        ILogger logger,
        string adapterName,
        int earlyRenewSeconds = 300)
    {
        _refreshFunc = Check.NotNull(refreshFunc);
        _logger = Check.NotNull(logger);
        _adapterName = Check.NotNullOrWhiteSpace(adapterName);
        _earlyRenewSeconds = earlyRenewSeconds;
    }

    /// <summary>
    /// 获取有效 token。缓存命中时直接返回；过期时在锁内刷新（双重检查）。
    /// </summary>
    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // _token/_expiry 都在锁内读写，保证跨线程一致可见。
        // （无锁快速路径需要对 _token+_expiry 做 volatile/Volatile.Read 才安全；token
        // 获取频率不高，统一进锁是更简单且完全正确的选择。）
        await _lock.WaitAsync(ct);
        try
        {
            if (_token != null && DateTime.UtcNow < _expiry)
                return _token;

            var (token, expiresIn) = await _refreshFunc(ct);
            _token = token;
            _expiry = DateTime.UtcNow.AddSeconds(expiresIn - _earlyRenewSeconds);

            _logger.LogDebug("{Adapter} access token refreshed, expires in {Seconds}s", _adapterName, expiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
