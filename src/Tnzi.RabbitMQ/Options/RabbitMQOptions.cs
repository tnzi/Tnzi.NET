namespace Tnzi.RabbitMQ.Options;

/// <summary>
/// RabbitMQ 事件总线配置选项
/// 配置路径：RabbitMQ
/// 注意：大部分配置已包含在 EventBusOptions 中，此 Options 仅用于 RabbitMQ 特定的高级配置
/// </summary>
public class RabbitMQOptions
{
    /// <summary>
    /// 连接配置选项
    /// </summary>
    public ConnectionOptions Connection { get; set; } = new();

    /// <summary>
    /// 启动时按容器里已注册的 <c>IEventHandler&lt;T&gt;</c> 自动订阅对应的集成事件（默认: true）。
    /// </summary>
    /// <remarks>
    /// 关掉它意味着这个进程只发布、不消费，除非应用自己调用 <c>SubscribeEventAsync</c>。
    /// 纯生产者服务可以关；但要清楚：交换机上没有队列绑定时，发出去的消息会被代理丢弃
    /// （本模块用 <c>mandatory: true</c> 发布，这种情况会记一条 Error）。
    /// </remarks>
    public bool AutoSubscribe { get; set; } = true;

    /// <summary>
    /// 消费者预取数量（默认: 10）：每个消费者 Channel 上<b>未确认</b>消息的上限（<c>basic.qos</c>）。
    /// </summary>
    /// <remarks>
    /// 它限制的是代理往客户端推了多少条还没 ACK 的消息，不是「同时处理」的条数 ——
    /// 同时处理多少条由 <see cref="ConsumerDispatchConcurrency"/> 决定。预取 10 条而派发并发度为 1，
    /// 其余 9 条只是躺在客户端缓冲里等前一条的回调返回。
    /// </remarks>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// 每个消费者 Channel 的派发并发度（默认: <see langword="null"/> = 跟随 <see cref="PrefetchCount"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// RabbitMQ.Client 7 的默认值是 1：同一 Channel 上的投递串行派发，派发器等上一条的回调返回才取下一条。
    /// 重试退避的 <c>Task.Delay</c>（默认 1s / 2s / 4s，上限 30s）就跑在回调里 —— 一条失败消息按指数退避
    /// 阻塞该事件类型的<b>全部</b>消费，包括与它毫不相干的消息；下游短暂不可用时吞吐掉到大约每个退避间隔一条。
    /// </para>
    /// <para>
    /// 并发度大于 1 时同一队列内的处理顺序不再有保证 —— 本模块从未承诺过顺序（at-least-once、处理器必须幂等），
    /// 需要严格顺序的事件类型自己设成 1。只作用于消费者 Channel，发布 Channel 不受影响。
    /// </para>
    /// </remarks>
    public ushort? ConsumerDispatchConcurrency { get; set; }

    /// <summary>
    /// 消息最大重试次数（默认: 3）
    /// 超过此次数后消息将被发送到死信队列
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 死信交换机名称（默认: "Tnzi.Events.DeadLetter"）
    /// </summary>
    public string DeadLetterExchange { get; set; } = "Tnzi.Events.DeadLetter";

    /// <summary>
    /// Retry delay options for exponential backoff before republishing failed messages.
    /// </summary>
    public RetryDelayOptions RetryDelay { get; set; } = new();

    /// <summary>
    /// Channel pool options for high-throughput publish scenarios.
    /// </summary>
    public ChannelPoolOptions ChannelPool { get; set; } = new();
}

/// <summary>
/// Retry delay options with exponential backoff.
/// Used when republishing failed messages to introduce a delay before retrying.
/// </summary>
public class RetryDelayOptions
{
    /// <summary>
    /// Whether exponential backoff is enabled (default: true).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Initial delay in milliseconds for the first retry (default: 1000).
    /// </summary>
    public int InitialDelayMs { get; set; } = 1000;

    /// <summary>
    /// Multiplier applied to the delay for each subsequent retry (default: 2.0).
    /// </summary>
    public double Multiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum delay in milliseconds, capping the exponential growth (default: 30000).
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;

    /// <summary>
    /// Calculate the delay for a given retry attempt (1-based).
    /// </summary>
    public TimeSpan GetDelay(int retryAttempt)
    {
        if (!Enabled || retryAttempt <= 0)
            return TimeSpan.Zero;

        var delayMs = InitialDelayMs * Math.Pow(Multiplier, retryAttempt - 1);
        delayMs = Math.Min(delayMs, MaxDelayMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }
}

/// <summary>
/// Channel pool options for high-throughput publish scenarios.
/// When enabled, multiple publish channels are pooled to increase throughput.
/// </summary>
public class ChannelPoolOptions
{
    /// <summary>
    /// Whether the channel pool is enabled (default: false).
    /// Enable for high-throughput scenarios where a single publish channel is a bottleneck.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Maximum number of channels in the pool (default: 5, range: 1-50).
    /// </summary>
    public int MaxSize { get; set; } = 5;
}

/// <summary>
/// RabbitMQ 连接配置选项
/// </summary>
public class ConnectionOptions
{
    /// <summary>
    /// 是否启用自动恢复（默认: true）
    /// </summary>
    public bool AutomaticRecoveryEnabled { get; set; } = true;

    /// <summary>
    /// 网络恢复间隔（秒，默认: 10）
    /// </summary>
    public int NetworkRecoveryIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// 连接超时时间（毫秒，默认: 30000）
    /// </summary>
    public int RequestedConnectionTimeout { get; set; } = 30000;

    /// <summary>
    /// 心跳超时时间（秒，默认: 60）
    /// </summary>
    public int RequestedHeartbeat { get; set; } = 60;
}
