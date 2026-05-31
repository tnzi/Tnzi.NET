namespace Tnzi.Kafka.Options;

/// <summary>
/// Kafka 事件总线配置选项
/// 配置路径：Kafka
/// </summary>
public class KafkaOptions
{
    /// <summary>
    /// Kafka Broker 地址（逗号分隔多个地址，如 "localhost:9092,localhost:9093"）
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// 主题名称前缀（默认: "Tnzi.Events"）
    /// </summary>
    public string TopicPrefix { get; set; } = "Tnzi.Events";

    /// <summary>
    /// 消费者组 ID 前缀（默认: "Tnzi.EventBus"）
    /// </summary>
    public string GroupIdPrefix { get; set; } = "Tnzi.EventBus";

    /// <summary>
    /// 生产者配置
    /// </summary>
    public KafkaProducerOptions Producer { get; set; } = new();

    /// <summary>
    /// 消费者配置
    /// </summary>
    public KafkaConsumerOptions Consumer { get; set; } = new();
}

/// <summary>
/// Kafka 生产者配置选项
/// </summary>
public class KafkaProducerOptions
{
    /// <summary>
    /// 消息发送失败重试次数（默认: 3）
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// 重试退避时间（毫秒，默认: 100）
    /// </summary>
    public int RetryBackoffMs { get; set; } = 100;

    /// <summary>
    /// 消息超时时间（毫秒，默认: 30000）
    /// </summary>
    public int MessageTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 是否启用幂等性（默认: true）
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>
    /// Acks 确认模式（默认: All）
    /// 可选值：None(0)、Leader(1)、All(-1)
    /// </summary>
    public Acks Acks { get; set; } = Acks.All;
}

/// <summary>
/// Kafka 消费者配置选项
/// </summary>
public class KafkaConsumerOptions
{
    /// <summary>
    /// 自动偏移重置策略（默认: Earliest）
    /// </summary>
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;

    /// <summary>
    /// 是否启用自动提交（默认: false，使用手动提交）
    /// </summary>
    public bool EnableAutoCommit { get; set; }

    /// <summary>
    /// 会话超时时间（毫秒，默认: 45000）
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 45000;

    /// <summary>
    /// 消费者最大重连次数（默认: 10，设为 0 禁用重连）
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// 重连初始退避时间（秒，默认: 1）
    /// 使用指数退避策略，每次重连退避时间翻倍，直到达到 MaxReconnectBackoffSeconds
    /// </summary>
    public int InitialReconnectBackoffSeconds { get; set; } = 1;

    /// <summary>
    /// 重连最大退避时间（秒，默认: 60）
    /// </summary>
    public int MaxReconnectBackoffSeconds { get; set; } = 60;

    /// <summary>
    /// 处理器失败时的进程内最大重试次数（默认: 3，设为 0 表示失败即耗尽）。
    /// 重试期间不提交偏移量，避免静默丢消息。
    /// </summary>
    public int MaxConsumeRetries { get; set; } = 3;

    /// <summary>
    /// 处理器失败重试的退避时间（毫秒，默认: 500）。
    /// </summary>
    public int ConsumeRetryBackoffMs { get; set; } = 500;

    /// <summary>
    /// 是否启用死信投递（默认: true）。
    /// 启用时：重试耗尽后将原始消息投递到死信主题并提交偏移量（毒消息移走、分区继续推进）。
    /// 禁用时：重试耗尽后不提交偏移量，等待重投（at-least-once，绝不静默丢弃）。
    /// </summary>
    public bool DeadLetterEnabled { get; set; } = true;

    /// <summary>
    /// 死信主题后缀（默认: ".dlq"），最终死信主题为 "{原主题}{后缀}"。
    /// </summary>
    public string DeadLetterTopicSuffix { get; set; } = ".dlq";
}
