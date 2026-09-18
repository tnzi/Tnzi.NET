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
    /// 是否仍持有锁。获取成功的句柄初始为 true；已释放、或实现在持有期间判定锁已丢失后为 false。
    /// </summary>
    /// <remarks>
    /// 它是一个瞬时快照：获取成功那一刻恒为 true，之后可能翻转。长临界区不要靠反复轮询它来发现丢锁，
    /// 把 <see cref="Lost"/> 链进自己的取消令牌。
    /// </remarks>
    bool IsAcquired { get; }

    /// <summary>
    /// 锁在持有期间被判定丢失（续租失败、被抢占、后端不可达）时取消的令牌。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 持锁者应把它与自己的取消令牌链接起来（<c>CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, handle.Lost)</c>）
    /// 传给临界区：锁一丢，另一个实例随时可能抢到锁并读到同一批数据，把这一批跑完只会做出互斥本来要防的那件事。
    /// </para>
    /// <para>
    /// 正常释放（<see cref="IAsyncDisposable.DisposeAsync"/>）不会取消它 —— 「干完了」与「被抢了」是两个信号。
    /// 不检测丢失的实现（固定过期语义、没有看门狗）返回 <see cref="CancellationToken.None"/>：永不取消，链接它没有代价。
    /// </para>
    /// </remarks>
    CancellationToken Lost => CancellationToken.None;
    
    /// <summary>
    /// 延长锁的持有时间
    /// </summary>
    /// <param name="extension">延长的时间</param>
    /// <returns>是否成功延长</returns>
    Task<bool> ExtendAsync(TimeSpan extension);
}
