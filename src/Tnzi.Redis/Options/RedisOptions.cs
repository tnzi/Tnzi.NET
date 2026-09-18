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
    /// 所有实例读的是同一份数据，没有本地副本需要失效。此前它无条件发布，
    /// 而 <c>SubscribeCacheInvalidationAsync</c> 在全仓<b>零调用方</b> —— 纯粹的开销。
    /// </para>
    /// <para>
    /// 打开它的正当场景：消费方在 Redis 之上自建了本地 L1 缓存，需要在别的实例写入时清掉自己那份。
    /// 那种情况下打开本开关，并调用 <see cref="Tnzi.Caching.ICacheSyncService.SubscribeCacheInvalidationAsync"/>
    /// 订阅。关闭时 <c>ICacheSyncService</c> <b>不注册</b>，注入它的地方会拿到 <see langword="null"/>。
    /// </para>
    /// <para>
    /// 打开后<b>每一条</b>写入 / 删除路径都广播（含同步重载）：Set / SetMany / SetWithTags / TrySet 发
    /// <c>Update</c>；Remove / RemoveMany / RemoveByPattern / RemoveByPrefix / RemoveByTag 对每个实际删掉的键
    /// 发 <c>Remove</c>（键已去掉实例前缀）；Clear 发<b>一条</b>键为 <c>*</c> 的 <c>Clear</c>，订阅方据此整体清空 L1。
    /// 一条覆盖不全的失效通道比没有更危险 —— 此前 RemoveByTag / RemoveByPrefix / Clear 与三个同步重载一条都不发，
    /// 而按标签批量失效恰是 L1 最需要广播的场景。发布是 fire-and-forget，失败只记 Warning。
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
