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
    /// 消费者预取数量（默认: 10）
    /// 控制消费者同时处理的消息数量上限
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

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
