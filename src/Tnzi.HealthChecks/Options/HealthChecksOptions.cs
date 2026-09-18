
namespace Tnzi.HealthChecks.Options;

/// <summary>
/// 健康检查配置选项
/// 配置路径：HealthChecks
/// </summary>
public class HealthChecksOptions
{
    /// <summary>
    /// 是否启用健康检查
    /// 默认：true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 健康检查端点路径（完整检查）
    /// 默认：/health
    /// </summary>
    public string Path { get; set; } = "/health";

    /// <summary>
    /// 存活探针路径（仅检查进程存活，无外部依赖检查）
    /// 用于 Kubernetes liveness probe
    /// 默认：/health/live
    /// </summary>
    public string LivenessPath { get; set; } = "/health/live";

    /// <summary>
    /// 就绪探针路径（检查所有依赖是否就绪）
    /// 用于 Kubernetes readiness probe
    /// 默认：/health/ready
    /// </summary>
    public string ReadinessPath { get; set; } = "/health/ready";

    /// <summary>
    /// 是否输出详细的 JSON 检查结果（逐项名称、状态、耗时与描述）。
    /// 默认：true。
    /// </summary>
    /// <remarks>
    /// ★ 详细输出<b>不再包含异常消息与检查项数据</b>：那两项要另外打开
    /// <see cref="ExposeErrorDetails"/>。此前它们随详细输出一起、默认对<b>匿名</b>
    /// 探针端点输出，于是一次数据库连接失败会把连接串片段发给任何调用方，
    /// 而文档写着「仅在非生产环境使用」—— 源码里没有任何环境判断来兑现这句话。
    /// </remarks>
    public bool DetailedOutput { get; set; } = true;

    /// <summary>
    /// 是否在详细输出里包含<b>异常消息</b>与各检查项的 <c>data</c>。默认：false。
    /// </summary>
    /// <remarks>
    /// 这些内容常带连接串片段、主机名、内部路径。探针端点默认匿名可访问，
    /// 因此只在受信网络（内网监控、开发环境）里打开。
    /// </remarks>
    public bool ExposeErrorDetails { get; set; }

    /// <summary>
    /// 健康检查超时时间（秒）
    /// 默认：10秒
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// 是否启用缓存健康检查（内存缓存）
    /// 默认：true
    /// </summary>
    public bool EnableCacheCheck { get; set; } = true;

    /// <summary>
    /// 是否启用数据库健康检查
    /// 默认：false（需要显式启用）
    /// 启用后会检查主 DbContext 的连接状态
    /// </summary>
    public bool EnableDatabaseCheck { get; set; } = false;

    /// <summary>
    /// 是否启用 Redis 健康检查
    /// 默认：false（需要显式启用）
    /// 仅当使用 Redis 缓存或分布式会话时需要启用
    /// </summary>
    public bool EnableRedisCheck { get; set; } = false;

    /// <summary>
    /// 是否启用事件总线健康检查
    /// 默认：false（需要显式启用）
    /// 仅当使用 RabbitMQ/Kafka 分布式事件总线时需要启用
    /// </summary>
    public bool EnableEventBusCheck { get; set; } = false;
}
