
namespace Tnzi.Redis.Options;

/// <summary>
/// Redis 配置选项验证器
/// </summary>
public class RedisOptionsValidator : OptionsValidatorBase<RedisOptions>
{
    /// <summary>
    /// 验证 Redis 配置选项
    /// </summary>
    protected override void ValidateOptions(RedisOptions options, List<string> errors)
    {
        // 验证连接选项
        if (options.Connection != null)
        {
            ValidateConnectionOptions(options.Connection, errors);
        }
        else
        {
            errors.Add("Redis.Connection cannot be null.");
        }
    }

    /// <summary>
    /// 验证连接选项
    /// </summary>
    private static void ValidateConnectionOptions(ConnectionOptions options, List<string> errors)
    {
        // 验证连接重试次数（允许为0，表示不重试）
        if (options.ConnectRetry < 0)
        {
            errors.Add("Redis.Connection.ConnectRetry must be greater than or equal to 0.");
        }

        // 验证连接超时（必须大于0）
        if (options.ConnectTimeout <= 0)
        {
            errors.Add("Redis.Connection.ConnectTimeout must be greater than 0.");
        }

        // 验证同步操作超时（必须大于0）
        if (options.SyncTimeout <= 0)
        {
            errors.Add("Redis.Connection.SyncTimeout must be greater than 0.");
        }
    }
}