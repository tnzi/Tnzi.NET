namespace Tnzi.Kafka.Options;

/// <summary>
/// Kafka 配置选项验证器
/// </summary>
public class KafkaOptionsValidator : OptionsValidatorBase<KafkaOptions>
{
    /// <summary>
    /// 验证 Kafka 配置选项
    /// </summary>
    protected override void ValidateOptions(KafkaOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            errors.Add("BootstrapServers cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(options.TopicPrefix))
        {
            errors.Add("TopicPrefix cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(options.GroupIdPrefix))
        {
            errors.Add("GroupIdPrefix cannot be null or empty.");
        }

        // 验证生产者配置
        if (options.Producer != null)
        {
            if (options.Producer.RetryCount < 0)
            {
                errors.Add("Producer.RetryCount must be greater than or equal to 0.");
            }

            if (options.Producer.RetryBackoffMs <= 0)
            {
                errors.Add("Producer.RetryBackoffMs must be greater than 0.");
            }

            if (options.Producer.MessageTimeoutMs <= 0)
            {
                errors.Add("Producer.MessageTimeoutMs must be greater than 0.");
            }
        }
        else
        {
            errors.Add("Producer configuration cannot be null.");
        }

        // 验证消费者配置
        if (options.Consumer != null)
        {
            if (options.Consumer.SessionTimeoutMs <= 0)
            {
                errors.Add("Consumer.SessionTimeoutMs must be greater than 0.");
            }

            if (options.Consumer.MaxReconnectAttempts < 0)
            {
                errors.Add("Consumer.MaxReconnectAttempts must be greater than or equal to 0.");
            }

            if (options.Consumer.InitialReconnectBackoffSeconds <= 0)
            {
                errors.Add("Consumer.InitialReconnectBackoffSeconds must be greater than 0.");
            }

            if (options.Consumer.MaxReconnectBackoffSeconds <= 0)
            {
                errors.Add("Consumer.MaxReconnectBackoffSeconds must be greater than 0.");
            }

            if (options.Consumer.InitialReconnectBackoffSeconds > options.Consumer.MaxReconnectBackoffSeconds)
            {
                errors.Add("Consumer.InitialReconnectBackoffSeconds must be less than or equal to Consumer.MaxReconnectBackoffSeconds.");
            }
        }
        else
        {
            errors.Add("Consumer configuration cannot be null.");
        }
    }
}
