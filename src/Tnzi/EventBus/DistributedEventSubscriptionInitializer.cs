using Microsoft.Extensions.Hosting;

namespace Tnzi.EventBus;

/// <summary>
/// 启动时把「已注册的集成事件处理器」变成真正的代理订阅。
/// </summary>
/// <remarks>
/// <para>
/// <b>被修复的缺陷</b>：RabbitMQ / Kafka 模块从来没有任何东西调用过它们的订阅方法 ——
/// 全仓唯一的调用点在测试里。于是按文档配好连接串、注册好 <see cref="IEventHandler{TEvent}"/>
/// 之后：RabbitMQ 那边交换机上<b>没有任何队列绑定</b>，发出去的消息被代理直接丢弃
/// （发布用的是 <c>mandatory: false</c>，连 <c>basic.return</c> 都不会回），
/// 日志里照常打一行 "Published…"，处理器永不执行，消息<b>永久消失</b>；
/// Kafka 那边消息留在主题里不丢，但同样没有人消费。
/// 两边文档都写着「消费端事件处理器无需修改」—— 那句话此前是假的。
/// </para>
/// <para>
/// <b>订阅面 = DI 里注册的处理器，而不是程序集里存在的处理器类型</b>：能不能处理一条消息
/// 取决于容器解析得出解析不出，按类型扫描会订阅到那些「写了但没注册」的处理器，
/// 结果是消息被拉下来、没人处理、然后被确认 —— 又一次静默丢弃。
/// </para>
/// <para>
/// <b>只订阅 <see cref="IIntegrationEvent"/></b>：代理上只可能出现经
/// <see cref="IDistributedEventBus"/> / <see cref="IIntegrationEventBus"/> 发出的消息，
/// 而服务层的 <c>PublishEventAsync</c> 只把集成事件路由到那里；进程内领域事件走本地总线，
/// 给它们建队列只会在共享的代理上留下一堆永远收不到消息的持久化队列。
/// 直接把非集成事件发到分布式总线的罕见用法仍可显式调用传输自己的订阅方法。
/// </para>
/// </remarks>
public sealed class DistributedEventSubscriptionInitializer : IHostedService
{
    private readonly IServiceCollection _services;
    private readonly IDistributedEventSubscriber _subscriber;
    private readonly ILogger<DistributedEventSubscriptionInitializer> _logger;
    private readonly bool _enabled;
    private readonly string _transportName;

    /// <summary>
    /// 初始化一个 <see cref="DistributedEventSubscriptionInitializer"/> 类型的新实例。
    /// </summary>
    /// <param name="services">构建容器用的服务集合（订阅面的唯一真相来源，启动时才枚举）。</param>
    /// <param name="subscriber">传输侧的订阅面。</param>
    /// <param name="logger">日志。</param>
    /// <param name="transportName">传输名称，只用于日志（RabbitMQ / Kafka）。</param>
    /// <param name="enabled">是否启用自动订阅。</param>
    public DistributedEventSubscriptionInitializer(
        IServiceCollection services,
        IDistributedEventSubscriber subscriber,
        ILogger<DistributedEventSubscriptionInitializer> logger,
        string transportName,
        bool enabled = true)
    {
        _services = Check.NotNull(services);
        _subscriber = Check.NotNull(subscriber);
        _logger = Check.NotNull(logger);
        _transportName = Check.NotNullOrWhiteSpace(transportName);
        _enabled = enabled;
    }

    /// <summary>
    /// 找出容器里注册了处理器的集成事件类型。
    /// </summary>
    /// <remarks>
    /// 在<b>启动时</b>枚举而不是在模块的配置阶段快照：分布式总线模块的 LoadOrder 很靠前
    /// （11），配置阶段拿到的集合还缺着后加载的业务模块注册的处理器。
    /// 容器构建之后这个集合不再变化，此刻枚举得到的就是全量。
    /// </remarks>
    public static IReadOnlyList<Type> DiscoverHandledIntegrationEventTypes(IServiceCollection services)
    {
        Check.NotNull(services);

        var found = new HashSet<Type>();

        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;

            if (!serviceType.IsGenericType
                || serviceType.IsGenericTypeDefinition
                || serviceType.GetGenericTypeDefinition() != typeof(IEventHandler<>))
            {
                continue;
            }

            var eventType = serviceType.GetGenericArguments()[0];

            // 开放泛型注册（IEventHandler<> 的类型参数还没填）没有具体事件类型可订阅
            if (eventType.IsGenericParameter || eventType.ContainsGenericParameters)
                continue;

            // 抽象事件类型没有自己的消息：路由键是具体类型的 FullName
            if (!eventType.IsClass || eventType.IsAbstract)
                continue;

            if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
                continue;

            found.Add(eventType);
        }

        // 排序只为让启动日志与测试断言稳定
        return found.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation(
                "{Transport} automatic event subscription is disabled; this process publishes but consumes nothing " +
                "unless the application subscribes explicitly.",
                _transportName);
            return;
        }

        var eventTypes = DiscoverHandledIntegrationEventTypes(_services);

        if (eventTypes.Count == 0)
        {
            // 只发不收是一种正当部署形态（例如纯生产者服务），但它与「处理器忘了注册」
            // 长得一模一样，所以必须留下一条能对账的记录。
            _logger.LogWarning(
                "{Transport} is configured but no IEventHandler<T> is registered for any integration event; " +
                "nothing will be consumed from the broker in this process.",
                _transportName);
            return;
        }

        foreach (var eventType in eventTypes)
        {
            // 刻意不吞异常：订阅不上就意味着这个进程收不到消息，而"启动成功但永远不消费"
            // 正是本类要修的那个缺陷。让它在启动时炸掉，由编排器重启或由人来看。
            await _subscriber.SubscribeEventAsync(eventType, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "{Transport} subscribed to {Count} integration event type(s): {EventTypes}",
            _transportName,
            eventTypes.Count,
            string.Join(", ", eventTypes.Select(t => t.FullName)));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
