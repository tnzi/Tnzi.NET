
namespace Tnzi.HealthChecks.Checks;

/// <summary>
/// 事件总线健康检查
/// 用于检测事件总线的连接状态
///
/// - 本地事件总线：始终返回 Healthy（无外部依赖）
/// - 分布式事件总线：返回 Degraded，因为 IEventBus 接口不支持连通性测试
///   需要具体实现（如 RabbitMQ/Kafka）提供更精确的健康检查
/// </summary>
public class EventBusHealthCheck : IHealthCheck
{
    private readonly IEventBus _eventBus;

    public EventBusHealthCheck(IEventBus eventBus)
    {
        _eventBus = Check.NotNull(eventBus);
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var eventBusType = _eventBus.GetType().Name;
            var assemblyName = _eventBus.GetType().Assembly.GetName().Name ?? "Unknown";

            var data = new Dictionary<string, object>
            {
                { "EventBusType", eventBusType },
                { "Assembly", assemblyName },
                { "IsLocal", _eventBus.IsLocal }
            };

            if (_eventBus.IsLocal)
            {
                // 本地事件总线始终健康，无外部依赖
                return Task.FromResult(HealthCheckResult.Healthy(
                    "Local EventBus is working (no external dependencies)",
                    data));
            }

            // 分布式事件总线（RabbitMQ/Kafka）
            // IEventBus 接口不提供连通性测试方法，标记为 Degraded 提示需要更精确的检查
            data["Note"] = "IEventBus does not support connectivity testing. " +
                           "Consider implementing a transport-specific health check for precise diagnostics.";

            return Task.FromResult(HealthCheckResult.Degraded(
                $"Distributed EventBus ({eventBusType}) is registered but connectivity cannot be verified",
                data: data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "EventBus check failed",
                ex));
        }
    }
}
