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

}

/// <summary>
/// Dashboard 配置选项
/// </summary>
public class DashboardConfigOptions
{
    /// <summary>
    /// 是否启用 Dashboard（默认: false，opt-in）
    /// </summary>
    /// <remarks>
    /// ★ 曾默认 true，而验证器把「开着 Dashboard 却没开授权」判成硬错误 —— 出厂默认自己就通不过验证，
    /// 只写 <c>[DependsOn(typeof(HangfireModule))]</c> 不配任何节的应用启动即崩（2026-09-12 修复）。
    /// 开 Dashboard 必须同时配 <see cref="EnableAuthorization"/> 与 <see cref="AllowedRoles"/>。
    /// </remarks>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Dashboard 路径（默认: /hangfire）
    /// </summary>
    public string Path { get; set; } = "/hangfire";

    /// <summary>
    /// 是否启用授权（默认: false；<see cref="Enabled"/> 为 true 时必须为 true，启动校验强制）
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
    /// 队列列表，按优先级从高到低。留空表示只处理 <c>default</c> 队列。
    /// </summary>
    /// <remarks>
    /// ★ <b>默认值必须是空列表</b>：.NET 的配置绑定对已有的集合是<b>追加</b>语义。若这里预置 <c>["default"]</c>，
    /// 配的 <c>["critical", "default"]</c> 会绑成 <c>["default", "critical", "default"]</c>：Hangfire 按列表顺序取队列，
    /// 优先级整个倒过来；只配 <c>["reports"]</c> 的专用服务器也照样去拉 <c>default</c> 队列的任务。
    /// 缺省值改在读取处补，读取方一律经 <see cref="ResolveQueues"/>。
    /// </remarks>
    public List<string> Queues { get; set; } = [];

    /// <summary>
    /// 取生效的队列列表：配了就用配的（去重、保持先后），没配只处理 <c>default</c>。
    /// </summary>
    public string[] ResolveQueues()
        => Queues is { Count: > 0 } configured ? configured.Distinct().ToArray() : ["default"];
}
