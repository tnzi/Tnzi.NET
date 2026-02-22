namespace Tnzi.EventBus;

/// <summary>
/// 事件总线模块配置选项
/// 配置路径：EventBus
/// </summary>
public class EventBusOptions
{
    /// <summary>
    /// 获取或设置 事件总线类型（Local, RabbitMQ, Kafka）
    /// </summary>
    public string Type { get; set; } = "Local";

    /// <summary>
    /// 获取或设置 是否启用事件持久化
    /// </summary>
    public bool EnablePersistence { get; set; } = false;

    /// <summary>
    /// 获取或设置 RabbitMQ连接字符串（当Type为RabbitMQ时使用）
    /// </summary>
    public string? RabbitMqConnectionString { get; set; }

    /// <summary>
    /// 获取或设置 RabbitMQ交换机名称
    /// </summary>
    public string? RabbitMqExchangeName { get; set; }

    /// <summary>
    /// 获取或设置 Kafka引导服务器（当Type为Kafka时使用）
    /// </summary>
    public string? KafkaBootstrapServers { get; set; }

    /// <summary>
    /// 获取或设置 Kafka主题名称
    /// </summary>
    public string? KafkaTopicName { get; set; }
    
    /// <summary>
    /// 获取或设置 本地事件总线的最大并发处理器数（默认10）
    /// </summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// 获取或设置 是否启用事件处理器自动注册（默认 true）
    /// </summary>
    public bool AutoRegisterHandlers { get; set; } = true;

    /// <summary>
    /// 获取或设置 要扫描的程序集名称列表（为空则扫描所有已加载的非系统程序集）
    /// 程序集名称格式：完整名称（如 "MyApp"）或简单名称（如 "MyApp"）
    /// </summary>
    public List<string> HandlerAssemblies { get; set; } = new();

    /// <summary>
    /// 获取或设置 自动注册的处理器默认生命周期（默认 Scoped）
    /// 可通过 EventHandlerLifetimeAttribute 特性覆盖单个处理器的生命周期
    /// </summary>
    public ServiceLifetime DefaultHandlerLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// 获取或设置 是否启用事件处理器重试机制
    /// 默认值：false（保持当前行为，不重试）
    /// 
    /// 当为 true 时，处理器失败后会根据 RetryCount 和 RetryInterval 进行重试
    /// </summary>
    public bool EnableRetry { get; set; } = false;

    /// <summary>
    /// 获取或设置 重试次数
    /// 默认值：3
    /// 
    /// 仅在 EnableRetry 为 true 时生效
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// 获取或设置 重试间隔（毫秒）
    /// 默认值：1000（1秒）
    /// 
    /// 使用指数退避策略：第 n 次重试的间隔 = RetryInterval * (2 ^ (n - 1))
    /// 仅在 EnableRetry 为 true 时生效
    /// </summary>
    public int RetryIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 获取或设置 是否启用死信队列
    /// 默认值：false
    /// 
    /// 当为 true 时，重试失败后的事件会被发送到死信队列
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = false;
}

