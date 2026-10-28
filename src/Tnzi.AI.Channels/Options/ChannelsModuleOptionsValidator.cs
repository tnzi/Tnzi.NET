namespace Tnzi.AI.Channels.Options;

/// <summary>
/// ChannelsModuleOptions 验证器
/// </summary>
public class ChannelsModuleOptionsValidator : OptionsValidatorBase<ChannelsModuleOptions>
{
    private readonly MultiTenancyOptions _multiTenancy;

    public ChannelsModuleOptionsValidator(IOptions<MultiTenancyOptions> multiTenancyOptions)
    {
        _multiTenancy = Check.NotNull(multiTenancyOptions).Value;
    }

    protected override void ValidateOptions(ChannelsModuleOptions options, List<string> errors)
    {
        if (!options.Enabled) return;

        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 100)
            errors.Add("MaxConcurrency must be between 1 and 100");

        // FileChannelThreadStore keys mappings by channel:chatId(:topicId) with no TenantId
        // component, so under multi-tenancy every tenant would share the same JSON file and
        // cross-contaminate thread mappings. Require the Database store (its unique index gains
        // a TenantId prefix when multi-tenancy is enabled) instead.
        if (_multiTenancy.Enabled && string.Equals(options.ThreadStore, "File", StringComparison.OrdinalIgnoreCase))
            errors.Add("ThreadStore=File is not tenant-isolated and cannot be used with multi-tenancy enabled; use ThreadStore=Database");

        if (options.Telegram.Enabled && string.IsNullOrWhiteSpace(options.Telegram.BotToken))
            errors.Add("Telegram.BotToken is required when Telegram adapter is enabled");

        if (options.Slack.Enabled && string.IsNullOrWhiteSpace(options.Slack.BotToken))
            errors.Add("Slack.BotToken is required when Slack adapter is enabled");

        if (options.Slack.Enabled && string.IsNullOrWhiteSpace(options.Slack.SigningSecret))
            errors.Add("Slack.SigningSecret is required when the Slack adapter is enabled (inbound webhook signatures cannot be verified otherwise)");

        if (options.Feishu.Enabled && string.IsNullOrWhiteSpace(options.Feishu.EncryptKey))
            errors.Add("Feishu.EncryptKey is required when the Feishu adapter is enabled (inbound webhook signatures cannot be verified otherwise)");

        if (options.Discord.Enabled && string.IsNullOrWhiteSpace(options.Discord.BotToken))
            errors.Add("Discord.BotToken is required when Discord adapter is enabled");

        if (options.Discord.Enabled && string.IsNullOrWhiteSpace(options.Discord.PublicKey))
            errors.Add("Discord.PublicKey is required when the Discord adapter is enabled (inbound webhook signatures cannot be verified otherwise)");

        if (options.Dingtalk.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Dingtalk.AppKey))
                errors.Add("Dingtalk.AppKey is required when DingTalk adapter is enabled");
            if (string.IsNullOrWhiteSpace(options.Dingtalk.AppSecret))
                errors.Add("Dingtalk.AppSecret is required when DingTalk adapter is enabled");
            if (string.IsNullOrWhiteSpace(options.Dingtalk.RobotCode))
                errors.Add("Dingtalk.RobotCode is required when the DingTalk adapter is enabled (both reply APIs name the sending robot by it)");
        }

        // 分块发送的上限：0 会让分块器一直切出空块、原文不减（零进展循环），负数则每次发送都抛异常；
        // 两种都是启动照常、每条回复都发不出去。
        ValidateSendLimits("Slack", options.Slack.Enabled, options.Slack.MaxMessageLength, options.Slack.MaxRetries, errors);
        ValidateSendLimits("Discord", options.Discord.Enabled, options.Discord.MaxMessageLength, options.Discord.MaxRetries, errors);
        ValidateSendLimits("Dingtalk", options.Dingtalk.Enabled, options.Dingtalk.MaxMessageLength, options.Dingtalk.MaxRetries, errors);
        ValidateSendLimits("Feishu", options.Feishu.Enabled, options.Feishu.MaxMessageLength, options.Feishu.MaxRetries, errors);

        // Telegram 的分块长度是平台上限常量（4096），只有重试次数可配
        if (options.Telegram.Enabled && options.Telegram.MaxRetries < 0)
            errors.Add("Telegram.MaxRetries must not be negative");
    }

    private static void ValidateSendLimits(string adapter, bool enabled, int maxMessageLength, int maxRetries, List<string> errors)
    {
        if (!enabled) return;

        if (maxMessageLength <= 0)
            errors.Add($"{adapter}.MaxMessageLength must be greater than 0");

        if (maxRetries < 0)
            errors.Add($"{adapter}.MaxRetries must not be negative");
    }
}
