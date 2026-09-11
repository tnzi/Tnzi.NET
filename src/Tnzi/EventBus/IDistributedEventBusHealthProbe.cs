namespace Tnzi.EventBus;

/// <summary>
/// 分布式事件总线的连通性探针，由各传输模块（RabbitMQ / Kafka）自己注册。
/// </summary>
/// <remarks>
/// <para>
/// <b>被修复的缺陷</b>：健康检查此前注入的是 <see cref="IEventBus"/>，而 2026-07-07 总线分离之后
/// 它<b>永远</b>是本地总线（<c>IsLocal</c> 恒 true）—— 于是那个检查恒返回 Healthy，
/// 判断分布式总线的那条分支永远走不到：代理宕机时 <c>/health/ready</c> 照样报健康，
/// 就绪探针对消息中间件的状态<b>一无所知</b>。
/// </para>
/// <para>
/// 契约放在核心而不是 HealthChecks 模块：传输模块（LoadOrder 11）比 HealthChecks（50）
/// 先加载，让它们反过来引用一个框架层模块会把依赖方向拧反；而两边本来都已经引用核心。
/// 没有任何传输注册探针时，健康检查只能诚实地报 Degraded —— 它确实无法验证连通性。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Transport health contribution is new; the shape may grow richer diagnostics")]
public interface IDistributedEventBusHealthProbe
{
    /// <summary>传输名称（RabbitMQ / Kafka），只用于诊断输出。</summary>
    string TransportName { get; }

    /// <summary>探测连通性。实现不应抛出：连不上就返回未连接并说明原因。</summary>
    Task<DistributedEventBusHealth> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 一次连通性探测的结果。
/// </summary>
[ExperimentalApi(Reason = "Transport health contribution is new; the shape may grow richer diagnostics")]
public sealed class DistributedEventBusHealth
{
    /// <summary>
    /// 初始化一个 <see cref="DistributedEventBusHealth"/> 类型的新实例。
    /// </summary>
    public DistributedEventBusHealth(bool isConnected, string? detail = null)
    {
        IsConnected = isConnected;
        Detail = detail;
    }

    /// <summary>是否连通。</summary>
    public bool IsConnected { get; }

    /// <summary>
    /// 诊断说明。
    /// </summary>
    /// <remarks>
    /// 未连通时应当写清是什么状态，但<b>不要</b>放凭据或连接串 ——
    /// 探针端点是匿名可访问的，这段文字会随详细输出一起出去。
    /// </remarks>
    public string? Detail { get; }
}
