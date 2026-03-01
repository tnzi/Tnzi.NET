namespace Tnzi.Threading;

/// <summary>
/// 按键异步锁 — 支持 per-key 排他锁，自动引用计数释放
/// 用于缓存防击穿、防重复提交、ID 生成协调等场景
/// </summary>
/// <remarks>
/// 使用示例:
/// <code>
/// var keyLock = new KeyedAsyncLock();
/// await using (await keyLock.LockAsync("user:123"))
/// {
///     // 该 key 的排他操作
/// }
/// </code>
/// </remarks>
public sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, RefCountedSemaphore> _locks = new();

    /// <summary>
    /// 获取指定 key 的异步排他锁
    /// </summary>
    /// <param name="key">锁的键名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>释放器，使用 await using 自动释放</returns>
    public async Task<IAsyncDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        var semaphore = GetOrAddSemaphore(key);

        try
        {
            await semaphore.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            ReleaseSemaphore(key, semaphore);
            throw;
        }

        return new KeyedLockReleaser(this, key, semaphore);
    }

    /// <summary>
    /// 尝试获取指定 key 的异步排他锁（带超时）
    /// </summary>
    /// <param name="key">锁的键名</param>
    /// <param name="timeout">等待超时</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>释放器（成功时）或 null（超时时）</returns>
    public async Task<IAsyncDisposable?> TryLockAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        var semaphore = GetOrAddSemaphore(key);

        bool acquired;
        try
        {
            acquired = await semaphore.Semaphore.WaitAsync(timeout, cancellationToken);
        }
        catch
        {
            ReleaseSemaphore(key, semaphore);
            throw;
        }

        if (!acquired)
        {
            ReleaseSemaphore(key, semaphore);
            return null;
        }

        return new KeyedLockReleaser(this, key, semaphore);
    }

    /// <summary>
    /// 获取当前活跃的锁数量（用于监控）
    /// </summary>
    public int ActiveLockCount => _locks.Count;

    private RefCountedSemaphore GetOrAddSemaphore(string key)
    {
        while (true)
        {
            var semaphore = _locks.GetOrAdd(key, _ => new RefCountedSemaphore());
            if (Interlocked.Increment(ref semaphore.RefCount) > 1 || _locks.ContainsKey(key))
            {
                return semaphore;
            }

            // Semaphore was removed between GetOrAdd and Increment, retry
            Interlocked.Decrement(ref semaphore.RefCount);
        }
    }

    private void ReleaseSemaphore(string key, RefCountedSemaphore semaphore)
    {
        if (Interlocked.Decrement(ref semaphore.RefCount) == 0)
        {
            // 最后一个引用，尝试移除
            _locks.TryRemove(new KeyValuePair<string, RefCountedSemaphore>(key, semaphore));
        }
    }

    private sealed class RefCountedSemaphore
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    private sealed class KeyedLockReleaser : IAsyncDisposable
    {
        private readonly KeyedAsyncLock _parent;
        private readonly string _key;
        private readonly RefCountedSemaphore _semaphore;
        private int _disposed;

        public KeyedLockReleaser(KeyedAsyncLock parent, string key, RefCountedSemaphore semaphore)
        {
            _parent = parent;
            _key = key;
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _semaphore.Semaphore.Release();
                _parent.ReleaseSemaphore(_key, _semaphore);
            }
            return ValueTask.CompletedTask;
        }
    }
}
