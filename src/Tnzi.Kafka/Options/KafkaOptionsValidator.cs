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
            // 自动提交会架空整套失败处置：重试期间「不提交」、死信关闭时「保留偏移量等重投」、
            // 毒消息「Seek 回原地卡住分区」全都依赖偏移量只由本模块提交。开着自动提交，
            // 客户端会在后台按周期越过它们把位置提交出去 —— 静默丢消息，而配置、日志看起来都正常。
            if (options.Consumer.EnableAutoCommit)
            {
                errors.Add("Consumer.EnableAutoCommit must be false: the consumer commits offsets itself after handlers succeed, "
                    + "and automatic commits would silently skip messages that are being retried, held for redelivery, or blocking a partition.");
            }

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

            if (options.Consumer.ConsumeErrorBackoffMs < 0)
            {
                errors.Add("Consumer.ConsumeErrorBackoffMs must be greater than or equal to 0.");
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
