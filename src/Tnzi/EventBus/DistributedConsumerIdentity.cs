namespace Tnzi.EventBus;

/// <summary>
/// 分布式订阅的身份：这个进程属于哪个<b>消费者组</b>（服务），又是哪个<b>实例</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>被修复的缺陷</b>：RabbitMQ 的队列名与 Kafka 的消费者组此前只由<b>事件类型</b>决定
/// （<c>Tnzi.Events.{事件全名}</c> / <c>Tnzi.EventBus.{事件全名}</c>），不含任何应用或实例成分。
/// 于是同一应用的 N 个实例、以及同一个代理上任意两个 Tnzi 服务，声明的是同一条持久队列 / 同一个组，
/// 竞争消费：每条消息只到达其中一个进程。订单服务与通知服务各自只收到约一半的订单事件；
/// 框架自己的多实例配置广播（每个实例都得 reload）只到达至多一个其它实例 —— 没有异常、
/// 没有 <c>basic.return</c>（队列确实存在）、没有任何症状。
/// </para>
/// <para>
/// 命名规则集中在这里，两个传输<b>共用同一段代码</b>（沿用 <see cref="DistributedEventSubscriptionInitializer"/>
/// 的做法：共用一个类就没有再分叉的余地）：
/// </para>
/// <list type="bullet">
/// <item><b>工作队列</b>（普通 <see cref="IIntegrationEvent"/>）：<c>{ConsumerGroup}.{事件全名}</c>。
/// 同一服务的所有实例共用，代理在实例间分发；不同服务各得一条。</item>
/// <item><b>广播</b>（<see cref="IBroadcastIntegrationEvent"/>）：<c>{ConsumerGroup}.{事件全名}.{InstanceId}</c>。
/// 每个实例一条，每个实例各收一份。</item>
/// </list>
/// <para>
/// <see cref="ConsumerGroup"/> 来自 <c>EventBus:ConsumerGroup</c>，未配置时取入口程序集名 ——
/// 同一个应用的所有实例天然同名，两个不同的应用天然不同名。两个服务若<b>刻意</b>要共用一条工作队列
/// （不太可能是你想要的），把它们配成同一个组即可。
/// </para>
/// </remarks>
[StableApi(Since = "0.1.0")]
public sealed class DistributedConsumerIdentity
{
    /// <summary>
    /// 入口程序集也拿不到名字时（例如非托管宿主）的兜底组名。
    /// </summary>
    public const string FallbackConsumerGroup = "Tnzi.Events";

    /// <summary>
    /// 初始化一个 <see cref="DistributedConsumerIdentity"/> 类型的新实例。
    /// </summary>
    /// <param name="consumerGroup">消费者组名（服务身份），不能为空白。</param>
    /// <param name="instanceId">实例标识；缺省取 <see cref="TnziInstance.Id"/>。</param>
    public DistributedConsumerIdentity(string consumerGroup, Guid? instanceId = null)
    {
        ConsumerGroup = Check.NotNullOrWhiteSpace(consumerGroup).Trim();
        InstanceId = instanceId ?? TnziInstance.Id;
    }

    /// <summary>
    /// 消费者组名 = 服务身份。同一服务的所有实例同名。
    /// </summary>
    public string ConsumerGroup { get; }

    /// <summary>
    /// 本进程实例标识（<see cref="TnziInstance.Id"/>）。
    /// </summary>
    public Guid InstanceId { get; }

    /// <summary>
    /// 按配置解析身份：<see cref="EventBusOptions.ConsumerGroup"/> 优先，未配置取入口程序集名。
    /// </summary>
    public static DistributedConsumerIdentity FromOptions(EventBusOptions options)
    {
        Check.NotNull(options);
        return new DistributedConsumerIdentity(ResolveConsumerGroup(options.ConsumerGroup));
    }

    /// <summary>
    /// 解析消费者组名：显式配置 > 入口程序集名 > <see cref="FallbackConsumerGroup"/>。
    /// </summary>
    public static string ResolveConsumerGroup(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        return Assembly.GetEntryAssembly()?.GetName().Name ?? FallbackConsumerGroup;
    }

    /// <summary>
    /// 某个事件类型是不是广播事件（每个实例各收一份）。
    /// </summary>
    public static bool IsBroadcast(Type eventType)
    {
        Check.NotNull(eventType);
        return typeof(IBroadcastIntegrationEvent).IsAssignableFrom(eventType);
    }

    /// <summary>
    /// 描述本进程对某个事件类型的订阅：队列 / 消费者组该叫什么、是不是广播。
    /// </summary>
    public DistributedSubscription Describe(Type eventType)
    {
        Check.NotNull(eventType);

        var eventName = eventType.FullName ?? eventType.Name;
        var broadcast = IsBroadcast(eventType);
        var sharedName = $"{ConsumerGroup}.{eventName}";

        return new DistributedSubscription(
            eventType,
            eventName,
            broadcast,
            broadcast ? $"{sharedName}.{InstanceId:N}" : sharedName,
            $"{ConsumerGroup}.DeadLetter.{eventName}");
    }
}

/// <summary>
/// 本进程对某个事件类型的订阅描述，由 <see cref="DistributedConsumerIdentity.Describe"/> 产出。
/// </summary>
[StableApi(Since = "0.1.0")]
public sealed class DistributedSubscription
{
    /// <summary>
    /// 初始化一个 <see cref="DistributedSubscription"/> 类型的新实例。
    /// </summary>
    public DistributedSubscription(Type eventType, string eventName, bool isBroadcast, string consumerName, string deadLetterName)
    {
        EventType = Check.NotNull(eventType);
        EventName = Check.NotNullOrWhiteSpace(eventName);
        IsBroadcast = isBroadcast;
        ConsumerName = Check.NotNullOrWhiteSpace(consumerName);
        DeadLetterName = Check.NotNullOrWhiteSpace(deadLetterName);
    }

    /// <summary>事件类型。</summary>
    public Type EventType { get; }

    /// <summary>事件全名，同时是交换机 / 主题侧的路由键。</summary>
    public string EventName { get; }

    /// <summary>是否广播（每个实例各收一份）。</summary>
    public bool IsBroadcast { get; }

    /// <summary>
    /// 消费者名：RabbitMQ 用作队列名，Kafka 用作消费者组名（加 <c>GroupIdPrefix</c> 前缀）。
    /// 工作队列为 <c>{组}.{事件}</c>，广播为 <c>{组}.{事件}.{实例}</c>。
    /// </summary>
    public string ConsumerName { get; }

    /// <summary>
    /// 死信落点名（RabbitMQ 的死信队列名）。广播订阅不用它：一条过期的广播重放出来是有害的。
    /// </summary>
    public string DeadLetterName { get; }
}
