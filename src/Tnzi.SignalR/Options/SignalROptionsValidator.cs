
namespace Tnzi.SignalR.Options;

/// <summary>
/// SignalR 配置选项验证器
/// </summary>
public class SignalROptionsValidator : OptionsValidatorBase<SignalROptions>
{
    /// <summary>
    /// 验证 SignalR 配置选项
    /// </summary>
    protected override void ValidateOptions(SignalROptions options, List<string> errors)
    {
        // 验证 Hub 配置
        if (options.Hub != null)
        {
            ValidateHubOptions(options.Hub, errors);
        }
        else
        {
            errors.Add("SignalR.Hub cannot be null.");
        }

        // 验证 Backplane 配置
        if (options.Backplane != null)
        {
            ValidateBackplaneOptions(options.Backplane, errors);
        }

        // 验证 MessagePack 配置
        if (options.MessagePack != null)
        {
            ValidateMessagePackOptions(options.MessagePack, errors);
        }
        else
        {
            errors.Add("SignalR.MessagePack cannot be null.");
        }

        // 验证速率限制配置
        if (options.RateLimit != null)
        {
            ValidateRateLimitOptions(options.RateLimit, errors);
        }
        else
        {
            errors.Add("SignalR.RateLimit cannot be null.");
        }
    }

    private static void ValidateHubOptions(HubOptions options, List<string> errors)
    {
        // 验证心跳间隔
        if (options.KeepAliveInterval <= TimeSpan.Zero)
        {
            errors.Add("SignalR.Hub.KeepAliveInterval must be greater than zero.");
        }

        // 验证客户端超时时间
        if (options.ClientTimeoutInterval <= TimeSpan.Zero)
        {
            errors.Add("SignalR.Hub.ClientTimeoutInterval must be greater than zero.");
        }

        // 验证握手超时时间
        if (options.HandshakeTimeout <= TimeSpan.Zero)
        {
            errors.Add("SignalR.Hub.HandshakeTimeout must be greater than zero.");
        }

        // 验证最大接收消息大小
        if (options.MaximumReceiveMessageSize.HasValue && options.MaximumReceiveMessageSize.Value <= 0)
        {
            errors.Add("SignalR.Hub.MaximumReceiveMessageSize must be greater than 0 when specified.");
        }
    }

    private static void ValidateBackplaneOptions(BackplaneOptions options, List<string> errors)
    {
        if (options.Type == BackplaneType.Redis)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                errors.Add("SignalR.Backplane.ConnectionString is required when Type is Redis.");
            }

            if (string.IsNullOrWhiteSpace(options.ChannelPrefix))
            {
                errors.Add("SignalR.Backplane.ChannelPrefix cannot be empty when Type is Redis.");
            }
        }
    }

    private static void ValidateMessagePackOptions(MessagePackOptions options, List<string> errors)
    {
        // MessagePack 选项目前没有需要验证的数值范围
        // 如果需要验证，可以在这里添加
    }

    private static void ValidateRateLimitOptions(RateLimitOptions options, List<string> errors)
    {
        if (options.Enabled)
        {
            // 验证每用户最大连接数
            if (options.MaxConnectionsPerUser <= 0)
            {
                errors.Add("SignalR.RateLimit.MaxConnectionsPerUser must be greater than 0 when Enabled is true.");
            }

            // 验证每分钟最大消息数
            if (options.MaxMessagesPerMinute <= 0)
            {
                errors.Add("SignalR.RateLimit.MaxMessagesPerMinute must be greater than 0 when Enabled is true.");
            }

            // 验证封禁时长
            if (options.BanDuration <= TimeSpan.Zero)
            {
                errors.Add("SignalR.RateLimit.BanDuration must be greater than zero when Enabled is true.");
            }
        }
    }
}