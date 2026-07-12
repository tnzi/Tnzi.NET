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
    /// 获取或设置 自动扫描是否排除框架程序集（以 "Tnzi" 开头，默认 true）。
    ///
    /// 框架要求所有 Tnzi.* 程序集**手动注册** event handlers（参见
    /// `Tnzi/CLAUDE.md` Module Patterns）。如果同时被 auto-scan 扫到，
    /// EventBusModule (LoadOrder=110) 比应用模块 (LoadOrder >= 300) 先
    /// 跑，IsAlreadyRegistered 检查时手动注册还未发生，handler 通过
    /// `TryAddEnumerable` 注册一次；随后应用模块通过 `AddScoped` 又注册
    /// 一次。结果同一 handler 在 DI 容器里出现两个 descriptor，每次事件
    /// 发布触发两次执行 — 例如 `UserLoggedInEventHandler` 写两条登录日志。
    ///
    /// 设为 false 可恢复旧行为（同时扫描框架与应用程序集）。仅当 consumer
    /// 显式指定了 `HandlerAssemblies` 时本选项不生效（认为是显式选择）。
    /// </summary>
    public bool ExcludeFrameworkAssemblies { get; set; } = true;

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

