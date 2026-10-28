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
    /// 获取或设置 RabbitMQ连接字符串（当Type为RabbitMQ时使用；留空时 RabbitMQ 模块改读 ConnectionStrings:RabbitMQ）
    /// </summary>
    public string? RabbitMqConnectionString { get; set; }

    /// <summary>
    /// 获取或设置 RabbitMQ交换机名称
    /// </summary>
    public string? RabbitMqExchangeName { get; set; }

    /// <summary>
    /// 获取或设置 Kafka引导服务器（当Type为Kafka时使用；留空时 Kafka 模块依次改读 ConnectionStrings:Kafka、Kafka:BootstrapServers）
    /// </summary>
    public string? KafkaBootstrapServers { get; set; }

    /// <summary>
    /// 获取或设置 分布式订阅的消费者组名（= 这个服务的身份）。未配置时取入口程序集名。
    /// </summary>
    /// <remarks>
    /// RabbitMQ 队列名与 Kafka 消费者组都以它开头：<c>{ConsumerGroup}.{事件全名}</c>。
    /// 同一个服务的所有实例必须同名（它们共用一条工作队列，代理在实例间分发）；
    /// 两个不同的服务必须不同名（否则它们会竞争消费同一条队列，各自只收到一部分事件，
    /// 且没有任何症状）。入口程序集名天然满足这两条，所以通常不需要配置。
    /// 部署形态里入口程序集名会撞（例如两个服务由同一个宿主程序集加载）时才需要显式指定。
    /// </remarks>
    public string? ConsumerGroup { get; set; }
    
    /// <summary>
    /// 获取或设置 本地事件总线全进程同时在执行的同步处理器上限（默认10）
    /// </summary>
    /// <remarks>
    /// 处理器里再发布的嵌套事件沿用外层已持有的名额，不再排队（否则外层处理器占满名额时嵌套发布永远等不到）；
    /// 后台处理器不占名额，它们里面的再发布与顶层发布一样排队。
    /// </remarks>
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
    /// 获取或设置 自动扫描是否排除框架程序集（以 "Tnzi" 开头，默认 true）。
    ///
    /// 框架要求所有 Tnzi.* 程序集**手动注册** event handlers，其中一部分按配置
    /// 有条件地注册；auto-scan 会把模块刻意没注册的那些也加进来。
    /// （与手动注册重复的自动注册由 EventBusModule 在 PostConfigure 阶段撤下，
    /// 同一 handler 不会因为两种注册并存而每次事件执行两遍。）
    ///
    /// 设为 false 可恢复旧行为（同时扫描框架与应用程序集）。仅当 consumer
    /// 显式指定了 `HandlerAssemblies` 时本选项不生效（认为是显式选择）。
    /// </summary>
    public bool ExcludeFrameworkAssemblies { get; set; } = true;

    /// <summary>
    /// 获取或设置 自动注册的处理器默认生命周期（默认 Scoped）。
    /// 需要别的生命周期的处理器在模块里手动注册（手动注册的生命周期优先于自动注册）。
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

    /// <summary>
    /// 获取或设置 内存死信队列的最大容量（默认 1000）
    /// 超过容量时按失败时间驱逐最旧条目并记录警告,防止处理器持续失败导致内存无界增长
    /// 仅对内存实现生效;持久化实现(如基于 Outbox 的实现)不受此限制
    /// </summary>
    public int DeadLetterQueueCapacity { get; set; } = 1000;

    /// <summary>
    /// 获取或设置 是否启用事务感知发布（默认 true）
    ///
    /// 启用时,若 PublishAsync 发生在活跃的工作单元事务中(经 AmbientUnitOfWork 检测),
    /// 发布会自动延迟到事务提交后执行;事务回滚则丢弃该事件。
    /// 这使得业务代码中直接注入 IEventBus 发布也默认具备事务安全性,
    /// 避免"事务回滚但事件已发出"的幽灵事件。
    ///
    /// 设为 false 恢复旧行为：无论是否在事务中都立即发布(同步处理器在发布点内联执行)。
    /// 需要在事务内立即触发处理器的罕见场景才应关闭。
    /// </summary>
    public bool TransactionAwarePublish { get; set; } = true;
}

