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
