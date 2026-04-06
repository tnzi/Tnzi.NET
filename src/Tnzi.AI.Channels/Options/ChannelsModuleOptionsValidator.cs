namespace Tnzi.AI.Channels.Options;

/// <summary>
/// ChannelsModuleOptions 验证器
/// </summary>
public class ChannelsModuleOptionsValidator : OptionsValidatorBase<ChannelsModuleOptions>
{
    protected override void ValidateOptions(ChannelsModuleOptions options, List<string> errors)
    {
        if (!options.Enabled) return;

        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 100)
            errors.Add("MaxConcurrency must be between 1 and 100");

        if (options.StreamingThrottleMs < 100)
            errors.Add("StreamingThrottleMs must be at least 100ms");

        if (options.Telegram.Enabled && string.IsNullOrWhiteSpace(options.Telegram.BotToken))
            errors.Add("Telegram.BotToken is required when Telegram adapter is enabled");

        if (options.Slack.Enabled && string.IsNullOrWhiteSpace(options.Slack.BotToken))
            errors.Add("Slack.BotToken is required when Slack adapter is enabled");

        if (options.Discord.Enabled && string.IsNullOrWhiteSpace(options.Discord.BotToken))
            errors.Add("Discord.BotToken is required when Discord adapter is enabled");

        if (options.Dingtalk.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Dingtalk.AppKey))
                errors.Add("Dingtalk.AppKey is required when DingTalk adapter is enabled");
            if (string.IsNullOrWhiteSpace(options.Dingtalk.AppSecret))
                errors.Add("Dingtalk.AppSecret is required when DingTalk adapter is enabled");
        }
    }
}
