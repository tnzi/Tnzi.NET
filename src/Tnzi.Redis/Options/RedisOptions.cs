namespace Tnzi.Redis.Options;

/// <summary>
/// Redis 模块配置选项
/// 配置路径：Redis
/// </summary>
public class RedisOptions
{
    /// <summary>
    /// 连接字符串（可选，优先使用 Caching.RedisConnectionString）
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 连接选项
    /// </summary>
    public ConnectionOptions Connection { get; set; } = new();

    /// <summary>
    /// 分布式锁选项
    /// </summary>
    public LockOptions Lock { get; set; } = new();

    /// <summary>
    /// 是否在每次缓存写入/删除后经 Redis Pub/Sub 广播失效通知（默认: <see langword="false"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 默认关闭是因为<b>框架内没有任何订阅方</b>：<c>RedisCacheService</c> 本身就是共享缓存，
    /// 所有实例读的是同一份数据，没有本地副本需要失效。此前它在 7 条写路径上逐次
    /// <c>Task.Run</c> 发布通知（<c>RemoveByPatternAsync</c> 还是<b>逐键</b>发），
    /// 而 <c>SubscribeCacheInvalidationAsync</c> 在全仓<b>零调用方</b> —— 纯粹的开销。
    /// </para>
    /// <para>
    /// 打开它的正当场景：消费方在 Redis 之上自建了本地 L1 缓存，需要在别的实例写入时清掉自己那份。
    /// 那种情况下打开本开关，并调用 <see cref="Tnzi.Caching.ICacheSyncService.SubscribeCacheInvalidationAsync"/>
    /// 订阅。关闭时 <c>ICacheSyncService</c> <b>不注册</b>，注入它的地方会拿到 <see langword="null"/>。
    /// </para>
    /// </remarks>
    public bool PublishCacheInvalidation { get; set; }
}

/// <summary>
/// Redis 分布式锁选项
/// </summary>
public class LockOptions
{
    /// <summary>
    /// 锁的默认过期时间（秒，默认: 30）。
    /// 这是持锁者进程崩溃后锁自动释放的安全上限。
    /// </summary>
    public int DefaultExpirySeconds { get; set; } = 30;

    /// <summary>
    /// 是否启用锁的自动续租（默认: true）。
    /// 启用后，只要持锁句柄存活，后台看门狗会在锁过期前周期性续租，
    /// 避免长任务因固定过期时间而中途丢锁；进程崩溃时看门狗随之消失，锁仍会在
    /// <see cref="DefaultExpirySeconds"/> 内自动释放，因此不会造成死锁。
    /// 关闭后锁为固定过期语义（达到 <see cref="DefaultExpirySeconds"/> 即释放）。
    /// </summary>
    public bool EnableAutoRenewal { get; set; } = true;
}

/// <summary>
/// Redis 连接选项
/// </summary>
public class ConnectionOptions
{
    /// <summary>
    /// 连接失败时是否中止（默认: false）
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>
    /// 连接重试次数（默认: 3）
    /// </summary>
    public int ConnectRetry { get; set; } = 3;

    /// <summary>
    /// 连接超时（毫秒，默认: 5000）
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// 同步操作超时（毫秒，默认: 5000）
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;
}
