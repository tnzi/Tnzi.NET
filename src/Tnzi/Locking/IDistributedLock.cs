namespace Tnzi.Locking;

/// <summary>
/// 分布式锁接口
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// 获取锁
    /// </summary>
    /// <param name="key">锁的键名</param>
    /// <param name="timeout">获取锁的超时时间，null 表示立即返回</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>锁句柄，获取失败返回 null</returns>
    Task<IDistributedLockHandle?> AcquireAsync(
        string key, 
        TimeSpan? timeout = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 尝试获取锁
    /// </summary>
    /// <param name="key">锁的键名</param>
    /// <param name="timeout">获取锁的超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功获取锁及锁句柄</returns>
    Task<(bool Success, IDistributedLockHandle? Handle)> TryAcquireAsync(
        string key, 
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 分布式锁句柄
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>
    /// 锁的键名
    /// </summary>
    string Key { get; }
    
    /// <summary>
    /// 是否已获取锁
    /// </summary>
    bool IsAcquired { get; }
    
    /// <summary>
    /// 延长锁的持有时间
    /// </summary>
    /// <param name="extension">延长的时间</param>
    /// <returns>是否成功延长</returns>
    Task<bool> ExtendAsync(TimeSpan extension);
}
