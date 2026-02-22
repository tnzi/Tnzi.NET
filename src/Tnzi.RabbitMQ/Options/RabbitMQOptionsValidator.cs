
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
    }
}