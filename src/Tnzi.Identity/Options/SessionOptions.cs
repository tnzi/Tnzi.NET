namespace Tnzi.Identity.Options;

/// <summary>
/// 会话存储类型
/// </summary>
public enum SessionStorageType
{
    /// <summary>
    /// 数据库存储（默认，适合简单项目）
    /// </summary>
    Database,

    /// <summary>
    /// Redis分布式存储（适合高并发/分布式场景）
    /// </summary>
    Redis
}

/// <summary>
/// 会话配置选项
/// 配置路径：Identity:Session
/// </summary>
public class SessionOptions
{
    /// <summary>
    /// 会话存储类型
    /// 默认：Database（数据库存储）
    /// 可选：Redis（分布式存储，需要配置 Redis 模块）
    /// </summary>
    public SessionStorageType StorageType { get; set; } = SessionStorageType.Database;

    /// <summary>
    /// 会话过期时间（分钟）
    /// 默认：60分钟
    /// 设置为0表示不过期（由 AccountSecurity.SessionTimeoutMinutes 控制）
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// 是否启用滑动过期
    /// 默认：true（每次活动更新过期时间）
    /// </summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>
    /// Redis会话键前缀
    /// 默认：Tnzi:Session
    /// 仅当 StorageType 为 Redis 时生效
    /// </summary>
    public string RedisKeyPrefix { get; set; } = "Tnzi:Session";

    /// <summary>
    /// 是否在数据库中保留会话记录（用于审计）
    /// 默认：false
    /// 仅当 StorageType 为 Redis 时生效
    /// 启用后，会话信息同时写入 Redis 和数据库
    /// </summary>
    public bool KeepDatabaseAuditLog { get; set; } = false;
}

