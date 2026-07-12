
namespace Tnzi.Redis.Locking;

/// <summary>
/// 基于 Redis 的分布式锁实现
/// </summary>
/// <remarks>
/// 锁过期时间由 <see cref="LockOptions.DefaultExpirySeconds"/> 控制（默认 30 秒）。
/// 当 <see cref="LockOptions.EnableAutoRenewal"/> 为 true（默认）时，持锁句柄会启动后台看门狗，
/// 在锁过期前周期性续租，使长任务不会因固定过期时间而中途丢锁；持锁进程崩溃时看门狗随之消失，
/// 锁仍会在过期时间内自动释放，因此不会造成死锁。关闭续租则为固定过期语义。
/// </remarks>
public class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _defaultExpiry;
    private readonly bool _autoRenew;
    private readonly ILoggerFactory? _loggerFactory;

    public RedisDistributedLock(
        IConnectionMultiplexer redis,
        LockOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        _redis = Check.NotNull(redis);
        options ??= new LockOptions();
        _defaultExpiry = TimeSpan.FromSeconds(options.DefaultExpirySeconds > 0 ? options.DefaultExpirySeconds : 30);
        _autoRenew = options.EnableAutoRenewal;
        _loggerFactory = loggerFactory;
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
                    return CreateHandle(db, lockKey, lockValue);
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
                return CreateHandle(db, lockKey, lockValue);
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

    private RedisDistributedLockHandle CreateHandle(IDatabase db, string lockKey, string lockValue)
    {
        var logger = _loggerFactory?.CreateLogger<RedisDistributedLockHandle>();
        return new RedisDistributedLockHandle(db, lockKey, lockValue, _defaultExpiry, _autoRenew, logger);
    }
}

/// <summary>
/// Redis 分布式锁句柄
/// </summary>
public class RedisDistributedLockHandle : IDistributedLockHandle
{
    // 释放锁：仅当 value 匹配时才删除（避免误删他人重新获取的同名锁）
    private const string ReleaseScript = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end";

    // 续租：仅当 value 匹配时才延长过期时间
    private const string ExtendScript = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('pexpire', KEYS[1], ARGV[2])
            else
                return 0
            end";

    private readonly IDatabase _db;
    private readonly string _lockKey;
    private readonly string _lockValue;
    private readonly TimeSpan _expiry;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource? _renewalCts;
    private readonly Task? _renewalTask;
    private volatile bool _isDisposed;
    private volatile bool _lockLost;

    public string Key => _lockKey.StartsWith("lock:") ? _lockKey[5..] : _lockKey;

    /// <summary>
    /// 是否仍持有锁。已释放或续租失败（锁丢失）后为 false。
    /// </summary>
    public bool IsAcquired => !_isDisposed && !_lockLost;

    public RedisDistributedLockHandle(IDatabase db, string lockKey, string lockValue)
        : this(db, lockKey, lockValue, TimeSpan.FromSeconds(30), autoRenew: false, logger: null)
    {
    }

    public RedisDistributedLockHandle(
        IDatabase db,
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        bool autoRenew,
        ILogger? logger)
    {
        _db = Check.NotNull(db);
        _lockKey = Check.NotNullOrWhiteSpace(lockKey);
        _lockValue = Check.NotNullOrWhiteSpace(lockValue);
        _expiry = expiry > TimeSpan.Zero ? expiry : TimeSpan.FromSeconds(30);
        _logger = logger;

        if (autoRenew)
        {
            _renewalCts = new CancellationTokenSource();
            // 后台看门狗，续租失败会自行停止；异常在循环内被吞掉，不会成为未观察任务异常
            _renewalTask = RenewLoopAsync(_renewalCts.Token);
        }
    }

    /// <summary>
    /// 后台续租循环：以过期时间的 1/3 为间隔（下限 1 秒）周期性续租。
    /// </summary>
    private async Task RenewLoopAsync(CancellationToken cancellationToken)
    {
        var intervalMs = Math.Max(1000, _expiry.TotalMilliseconds / 3);
        var interval = TimeSpan.FromMilliseconds(intervalMs);

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var extended = await ExtendCoreAsync();
                if (!extended)
                {
                    // 锁已丢失（被抢占或 Redis 不可用），停止续租并标记
                    _lockLost = true;
                    _logger?.LogWarning("Distributed lock '{Key}' auto-renewal failed; the lock is considered lost.", Key);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose 触发的正常取消，忽略
        }
        catch (Exception ex)
        {
            _lockLost = true;
            _logger?.LogWarning(ex, "Distributed lock '{Key}' auto-renewal loop terminated unexpectedly.", Key);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExtendAsync(TimeSpan extension)
    {
        if (_isDisposed) return false;

        var result = await _db.ScriptEvaluateAsync(
            ExtendScript,
            new RedisKey[] { _lockKey },
            new RedisValue[] { _lockValue, (long)extension.TotalMilliseconds });

        return (long)result! == 1;
    }

    /// <summary>
    /// 内部续租：使用配置的过期时间续租（供看门狗调用，不检查 _isDisposed）。
    /// </summary>
    private async Task<bool> ExtendCoreAsync()
    {
        var result = await _db.ScriptEvaluateAsync(
            ExtendScript,
            new RedisKey[] { _lockKey },
            new RedisValue[] { _lockValue, (long)_expiry.TotalMilliseconds });

        return (long)result! == 1;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // 先停止续租看门狗，再释放锁，避免释放后又被续租续上
        if (_renewalCts != null)
        {
            await _renewalCts.CancelAsync();
            try
            {
                if (_renewalTask != null)
                {
                    await _renewalTask;
                }
            }
            catch (OperationCanceledException)
            {
                // 预期内
            }
            _renewalCts.Dispose();
        }

        // 使用 Lua 脚本确保原子性：只有当 value 匹配时才删除
        await _db.ScriptEvaluateAsync(
            ReleaseScript,
            new RedisKey[] { _lockKey },
            new RedisValue[] { _lockValue });
    }
}
