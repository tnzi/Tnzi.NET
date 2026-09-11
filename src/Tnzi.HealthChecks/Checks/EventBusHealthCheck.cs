
namespace Tnzi.HealthChecks.Checks;

/// <summary>
/// 事件总线健康检查。
/// </summary>
/// <remarks>
/// <para>
/// 三种结果，各自对应一件不同的事实：
/// </para>
/// <list type="bullet">
/// <item>没有分布式总线 → <b>Healthy</b>：只有本地总线，无外部依赖，没什么可坏的。</item>
/// <item>有分布式总线但没有传输探针 → <b>Degraded</b>：连通性确实<b>无法验证</b>，如实说。</item>
/// <item>有探针 → 按探测结果 <b>Healthy / Unhealthy</b>：代理连不上就该让就绪探针失败。</item>
/// </list>
/// <para>
/// ★ <b>被修复的缺陷</b>：这个检查此前注入 <see cref="IEventBus"/>，而 2026-07-07 总线分离后
/// 它<b>永远</b>解析为本地总线（<c>IsLocal</c> 恒 true），于是恒返回 Healthy、判断分布式总线的
/// 那条分支<b>不可达</b>。代理宕机时 <c>/health/ready</c> 照样报健康 —— 一个永远说"好"的
/// 就绪探针比没有探针更糟：它会让编排器把流量继续送进一个收不到消息的实例。
/// </para>
/// </remarks>
public class EventBusHealthCheck : IHealthCheck
{
    private readonly IEventBus _localEventBus;
    private readonly IDistributedEventBus? _distributedEventBus;
    private readonly IReadOnlyList<IDistributedEventBusHealthProbe> _probes;

    public EventBusHealthCheck(
        IEventBus localEventBus,
        IEnumerable<IDistributedEventBusHealthProbe> probes,
        IDistributedEventBus? distributedEventBus = null)
    {
        _localEventBus = Check.NotNull(localEventBus);
        _probes = Check.NotNull(probes).ToList();
        _distributedEventBus = distributedEventBus;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            { "LocalEventBusType", _localEventBus.GetType().Name }
        };

        if (_distributedEventBus == null)
        {
            return HealthCheckResult.Healthy(
                "Local EventBus is working (no distributed event bus is registered)",
                data);
        }

        var distributedType = _distributedEventBus.GetType().Name;
        data["DistributedEventBusType"] = distributedType;

        if (_probes.Count == 0)
        {
            data["Note"] = "No IDistributedEventBusHealthProbe is registered, so broker connectivity cannot be verified. " +
                           "The shipped RabbitMQ and Kafka modules register one; a custom transport should too.";

            return HealthCheckResult.Degraded(
                $"Distributed EventBus ({distributedType}) is registered but connectivity cannot be verified",
                data: data);
        }

        var failures = new List<string>();

        foreach (var probe in _probes)
        {
            DistributedEventBusHealth result;
            try
            {
                result = await probe.CheckAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // 探针自己炸了 = 同样无法证明代理可用。当作不可用处理，
                // 因为"探测失败"与"代理不可用"对调用方的后果是一样的。
                failures.Add($"{probe.TransportName}: probe failed ({ex.GetType().Name})");
                continue;
            }

            data[$"{probe.TransportName}.Connected"] = result.IsConnected;
            if (result.Detail != null)
            {
                data[$"{probe.TransportName}.Detail"] = result.Detail;
            }

            if (!result.IsConnected)
            {
                failures.Add($"{probe.TransportName}: {result.Detail ?? "not connected"}");
            }
        }

        if (failures.Count > 0)
        {
            // Unhealthy 而不是 Degraded：这个实例收不到事件，不该继续被当作就绪
            return HealthCheckResult.Unhealthy(
                $"Distributed EventBus is not reachable ({string.Join("; ", failures)})",
                data: data);
        }

        return HealthCheckResult.Healthy(
            $"Distributed EventBus ({distributedType}) is connected",
            data);
    }
}
