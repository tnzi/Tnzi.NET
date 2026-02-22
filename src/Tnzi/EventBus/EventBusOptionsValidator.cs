
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

        // 验证最大并发数
        if (options.MaxConcurrency <= 0)
        {
            errors.Add("EventBus.MaxConcurrency must be greater than 0.");
        }

        // 条件验证：当使用 RabbitMQ 时，验证 RabbitMQ 配置
        if (string.Equals(options.Type, "RabbitMQ", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(options.RabbitMqConnectionString))
            {
                errors.Add("EventBus.RabbitMqConnectionString is required when Type is RabbitMQ.");
            }
        }

        // 条件验证：当使用 Kafka 时，验证 Kafka 配置
        if (string.Equals(options.Type, "Kafka", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(options.KafkaBootstrapServers))
            {
                errors.Add("EventBus.KafkaBootstrapServers is required when Type is Kafka.");
            }
        }

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