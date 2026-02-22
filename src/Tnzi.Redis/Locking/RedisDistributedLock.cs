
namespace Tnzi.Redis.Locking;

/// <summary>
/// 基于 Redis 的分布式锁实现
/// </summary>
public class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _defaultExpiry = TimeSpan.FromSeconds(30);

    public RedisDistributedLock(IConnectionMultiplexer redis)
    {
        _redis = Check.NotNull(redis);
    }

    /// <inheritdoc />
    public async Task<IDistributedLockHandle?> AcquireAsync(
        string key,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(key);

        var lockKey = $"lock:{key}";
        var lockValue = Guid.NewGuid().ToString();
        var db = _redis.GetDatabase();

        if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
        {
            // timeout 作为获取超时，锁过期使用 _defaultExpiry
            var endTime = DateTime.UtcNow.Add(timeout.Value);
            var retryDelay = TimeSpan.FromMilliseconds(50);

            while (DateTime.UtcNow < endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var acquired = await db.StringSetAsync(lockKey, lockValue, _defaultExpiry, When.NotExists);
                if (acquired)
                {
                    return new RedisDistributedLockHandle(db, lockKey, lockValue);
                }

                await Task.Delay(retryDelay, cancellationToken);
            }

            return null;
        }
        else
        {
            // 无超时，立即尝试一次
            var acquired = await db.StringSetAsync(lockKey, lockValue, _defaultExpiry, When.NotExists);
            if (acquired)
            {
                return new RedisDistributedLockHandle(db, lockKey, lockValue);
            }

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, IDistributedLockHandle? Handle)> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var handle = await AcquireAsync(key, timeout, cancellationToken);
        return (handle != null, handle);
    }
}

/// <summary>
/// Redis 分布式锁句柄
/// </summary>
public class RedisDistributedLockHandle : IDistributedLockHandle
{
    private readonly IDatabase _db;
    private readonly string _lockKey;
    private readonly string _lockValue;
    private bool _isDisposed;

    public string Key => _lockKey.StartsWith("lock:") ? _lockKey[5..] : _lockKey;
    public bool IsAcquired => !_isDisposed;

    public RedisDistributedLockHandle(IDatabase db, string lockKey, string lockValue)
    {
        _db = Check.NotNull(db);
        _lockKey = Check.NotNullOrWhiteSpace(lockKey);
        _lockValue = Check.NotNullOrWhiteSpace(lockValue);
    }

    /// <inheritdoc />
    public async Task<bool> ExtendAsync(TimeSpan extension)
    {
        if (_isDisposed) return false;

        // 使用 Lua 脚本确保原子性：只有当 value 匹配时才延长
        const string script = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('pexpire', KEYS[1], ARGV[2])
            else
                return 0
            end";

        var result = await _db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { _lockKey },
            new RedisValue[] { _lockValue, (long)extension.TotalMilliseconds });

        return (long)result! == 1;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // 使用 Lua 脚本确保原子性：只有当 value 匹配时才删除
        const string script = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end";

        await _db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { _lockKey },
            new RedisValue[] { _lockValue });
    }
}
