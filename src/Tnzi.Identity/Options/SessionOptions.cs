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

    /// <summary>
    /// 是否强制会话校验（多设备登录/单设备/限并发的真正生效开关）。
    /// 默认：true。启用后：登录签发的 access token 携带 session_id，JWT Bearer 的
    /// OnTokenValidated 每请求校验会话有效性（撤销/过期即 401），刷新令牌也校验会话。
    /// 设为 false 则退回旧行为（令牌仍带 session_id 但不校验），仅作应急逃生开关。
    /// </summary>
    public bool EnforceSessionValidation { get; set; } = true;

    /// <summary>
    /// 会话有效性校验的缓存秒数（仅数据库存储模式）。默认：30。
    /// OnTokenValidated 每请求校验会话，缓存避免每请求命中数据库；撤销时主动失效缓存，
    /// 故本地实例即时生效，跨实例（多节点共享数据库）最多滞后本值秒数。0 表示不缓存（每请求查库）。
    /// Redis 存储模式直接读缓存，不受此值影响。
    /// </summary>
    public int ValidationCacheSeconds { get; set; } = 30;

    /// <summary>
    /// 会话维护后台任务运行间隔（分钟）。默认：60。清理过期令牌 + 撤销长期失活会话，
    /// 避免幽灵会话累积影响并发计数。小于 5 时按 5 处理；0 表示禁用后台维护。
    /// </summary>
    public int MaintenanceIntervalMinutes { get; set; } = 60;
}

