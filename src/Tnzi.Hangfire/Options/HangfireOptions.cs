namespace Tnzi.Hangfire.Options;

/// <summary>
/// Hangfire 模块配置选项
/// 配置路径：Hangfire
/// </summary>
public class HangfireOptions
{
    /// <summary>
    /// 是否启用 Hangfire（默认: true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 存储类型（默认: Memory）
    /// </summary>
    public StorageType StorageType { get; set; } = StorageType.Memory;

    /// <summary>
    /// 连接字符串（Redis 或 SQL Server 时必填）
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Dashboard 配置
    /// </summary>
    public DashboardConfigOptions Dashboard { get; set; } = new();

    /// <summary>
    /// 服务器选项
    /// </summary>
    public ServerOptions Server { get; set; } = new();
}

/// <summary>
/// 存储类型
/// </summary>
public enum StorageType
{
    /// <summary>
    /// 内存存储（仅开发测试）
    /// </summary>
    Memory,

    /// <summary>
    /// Redis 存储
    /// </summary>
    Redis,

    /// <summary>
    /// SQL Server 存储
    /// </summary>
    SqlServer,

    /// <summary>
    /// PostgreSQL 存储
    /// </summary>
    PostgreSQL,

    /// <summary>
    /// MySQL 存储
    /// </summary>
    MySQL
}

/// <summary>
/// Dashboard 配置选项
/// </summary>
public class DashboardConfigOptions
{
    /// <summary>
    /// 是否启用 Dashboard（默认: true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Dashboard 路径（默认: /hangfire）
    /// </summary>
    public string Path { get; set; } = "/hangfire";

    /// <summary>
    /// 是否启用授权（默认: false，生产环境应启用）
    /// </summary>
    public bool EnableAuthorization { get; set; } = false;

    /// <summary>
    /// 允许访问的角色列表（启用授权时生效）
    /// </summary>
    public List<string> AllowedRoles { get; set; } = new();
}

/// <summary>
/// 服务器选项
/// </summary>
public class ServerOptions
{
    /// <summary>
    /// 工作进程数（默认: 5）
    /// </summary>
    public int WorkerCount { get; set; } = 5;

    /// <summary>
    /// 服务器名称（默认: 自动生成）
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// 队列列表（默认: default）
    /// </summary>
    public List<string> Queues { get; set; } = new() { "default" };
}
