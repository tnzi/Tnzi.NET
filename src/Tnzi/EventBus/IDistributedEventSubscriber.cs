namespace Tnzi.EventBus;

/// <summary>
/// 分布式事件总线的「按事件类型开始消费」面。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDistributedEventBus"/> 只规定了发布；<b>消费从来没有入口</b> ——
/// 各传输实现自带一个泛型订阅方法（RabbitMQ 的 <c>SubscribeEventAsync&lt;TEvent&gt;()</c>、
/// Kafka 的 <c>SubscribeEvent&lt;TEvent&gt;()</c>），而泛型方法没法按运行时 <see cref="Type"/> 调用，
/// 于是「按已注册的处理器自动订阅」这件事在两个模块里各写各的。本接口把那一面收口成
/// 一个非泛型方法，<see cref="DistributedEventSubscriptionInitializer"/> 因此能对两种传输
/// 用同一段代码 —— 两侧行为不再有分叉的余地。
/// </para>
/// <para>
/// 实现必须<b>幂等</b>：同一个事件类型订阅两次不得建出第二个消费者。启动期自动订阅与
/// 应用自己调用 <c>Subscribe</c> 会同时到达同一个类型，这不是异常路径。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Distributed consumption wiring is being unified across transports")]
public interface IDistributedEventSubscriber
{
    /// <summary>
    /// 开始消费某个事件类型（声明队列/主题、绑定、注册消费者）。
    /// </summary>
    /// <param name="eventType">事件类型，必须是实现 <see cref="IEvent"/> 的非抽象类。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SubscribeEventAsync(Type eventType, CancellationToken cancellationToken = default);
}
