
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
    /// 是否输出详细的 JSON 检查结果（逐项状态、耗时、描述与异常消息）。
    /// 默认：true。注意：探针端点是匿名可访问的，详细输出会把各检查项的异常消息
    /// 暴露给任何调用方；生产环境如不希望外泄内部错误细节，请显式设为 false。
    /// </summary>
    public bool DetailedOutput { get; set; } = true;

    /// <summary>
    /// 健康检查超时时间（秒）
    /// 默认：10秒
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// 详细输出响应缓存时长（秒）
    /// 避免高频健康检查请求导致的性能开销
    /// 默认：10秒
    /// </summary>
    public int CacheDurationSeconds { get; set; } = 10;

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
