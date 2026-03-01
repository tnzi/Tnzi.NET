
namespace Tnzi.RabbitMQ.Options;

/// <summary>
/// RabbitMQ 配置选项验证器
/// </summary>
public class RabbitMQOptionsValidator : OptionsValidatorBase<RabbitMQOptions>
{
    /// <summary>
    /// 验证 RabbitMQ 配置选项
    /// </summary>
    protected override void ValidateOptions(RabbitMQOptions options, List<string> errors)
    {
        // 验证连接配置
        if (options.Connection != null)
        {
            if (options.Connection.NetworkRecoveryIntervalSeconds <= 0)
            {
                errors.Add("RabbitMQ.Connection.NetworkRecoveryIntervalSeconds must be greater than 0.");
            }

            if (options.Connection.RequestedConnectionTimeout <= 0)
            {
                errors.Add("RabbitMQ.Connection.RequestedConnectionTimeout must be greater than 0.");
            }

            if (options.Connection.RequestedHeartbeat < 0)
            {
                errors.Add("RabbitMQ.Connection.RequestedHeartbeat must be greater than or equal to 0.");
            }
        }
        else
        {
            errors.Add("RabbitMQ.Connection cannot be null.");
        }

        // 验证 PrefetchCount（0 意味着无限制预取，可能导致消费者 OOM）
        if (options.PrefetchCount == 0)
        {
            errors.Add("RabbitMQ.PrefetchCount must be greater than 0. A value of 0 means unlimited prefetch which can cause memory issues.");
        }

        // 验证 MaxRetryCount
        if (options.MaxRetryCount < 0)
        {
            errors.Add("RabbitMQ.MaxRetryCount must be greater than or equal to 0.");
        }

        // 验证 DeadLetterExchange
        if (string.IsNullOrWhiteSpace(options.DeadLetterExchange))
        {
            errors.Add("RabbitMQ.DeadLetterExchange must not be empty.");
        }

        // 验证 RetryDelay
        if (options.RetryDelay != null)
        {
            if (options.RetryDelay.InitialDelayMs < 0)
            {
                errors.Add("RabbitMQ.RetryDelay.InitialDelayMs must be greater than or equal to 0.");
            }

            if (options.RetryDelay.Multiplier < 1.0)
            {
                errors.Add("RabbitMQ.RetryDelay.Multiplier must be greater than or equal to 1.0.");
            }

            if (options.RetryDelay.MaxDelayMs < options.RetryDelay.InitialDelayMs)
            {
                errors.Add("RabbitMQ.RetryDelay.MaxDelayMs must be greater than or equal to InitialDelayMs.");
            }
        }

        // 验证 ChannelPool
        if (options.ChannelPool is { Enabled: true })
        {
            if (options.ChannelPool.MaxSize < 1 || options.ChannelPool.MaxSize > 50)
            {
                errors.Add("RabbitMQ.ChannelPool.MaxSize must be between 1 and 50 when enabled.");
            }
        }
    }
}
