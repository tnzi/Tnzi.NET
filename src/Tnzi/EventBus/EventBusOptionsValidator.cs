
namespace Tnzi.EventBus;

/// <summary>
/// 事件总线配置选项验证器
/// </summary>
public class EventBusOptionsValidator : OptionsValidatorBase<EventBusOptions>
{
    /// <summary>
    /// 验证事件总线配置选项
    /// </summary>
    protected override void ValidateOptions(EventBusOptions options, List<string> errors)
    {
        // 验证事件总线类型
        if (!string.IsNullOrEmpty(options.Type))
        {
            var validTypes = new[] { "Local", "RabbitMQ", "Kafka" };
            if (!validTypes.Contains(options.Type, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"EventBus.Type must be one of: {string.Join(", ", validTypes)}.");
            }
        }

        // 消费者组：显式配了就不能是空白（空白会让队列名以 "." 开头，且所有实例都"同名"于一个空串）
        if (options.ConsumerGroup != null && string.IsNullOrWhiteSpace(options.ConsumerGroup))
        {
            errors.Add("EventBus.ConsumerGroup must not be blank when specified; omit it to use the entry assembly name.");
        }

        // 验证最大并发数
        if (options.MaxConcurrency <= 0)
        {
            errors.Add("EventBus.MaxConcurrency must be greater than 0.");
        }

        // 不在这里要求 RabbitMqConnectionString / KafkaBootstrapServers：传输模块还认 ConnectionStrings:RabbitMQ、
        // ConnectionStrings:Kafka 与 Kafka:BootstrapServers，这里只看得见 EventBus 节里的那一个。
        // 硬性要求它会让按文档只配备选来源的部署启动即失败；来源全缺由传输模块在解析处报错，消息列出它认的全部键。
        // 配了 Type 却没加载传输模块，由 EventBusModule 的启动自证拦下。

        // 验证 DefaultHandlerLifetime 的有效值
        if (!Enum.IsDefined(typeof(ServiceLifetime), options.DefaultHandlerLifetime))
        {
            errors.Add("EventBus.DefaultHandlerLifetime must be a valid ServiceLifetime value (Singleton, Scoped, or Transient).");
        }

        // 验证重试配置
        if (options.EnableRetry)
        {
            if (options.RetryCount <= 0)
            {
                errors.Add("EventBus.RetryCount must be greater than 0 when EnableRetry is true.");
            }

            if (options.RetryIntervalMs <= 0)
            {
                errors.Add("EventBus.RetryIntervalMs must be greater than 0 when EnableRetry is true.");
            }
        }
    }
}