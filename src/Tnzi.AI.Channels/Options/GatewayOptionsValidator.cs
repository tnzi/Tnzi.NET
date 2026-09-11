namespace Tnzi.AI.Channels.Options;

/// <summary>
/// GatewayOptions 验证器
/// </summary>
public class GatewayOptionsValidator : OptionsValidatorBase<GatewayOptions>
{
    protected override void ValidateOptions(GatewayOptions options, List<string> errors)
    {
        if (!options.Enabled) return;

        if (string.IsNullOrWhiteSpace(options.Path))
            errors.Add("Path must not be empty");

        if (options.MaxConnectionsPerUser < 1)
            errors.Add("MaxConnectionsPerUser must be at least 1");

        if (options.HeartbeatIntervalSeconds < 5)
            errors.Add("HeartbeatIntervalSeconds must be at least 5");

        if (options.SessionEvictionHours < 1)
            errors.Add("SessionEvictionHours must be at least 1");

        if (options.StreamingThrottleMs < 100)
            errors.Add("StreamingThrottleMs must be at least 100ms");

        // 下限 1 KiB：比这更小的上限连一条正常的 chat.send 都装不下，等于把端点关掉
        // 却仍然报告"已启用"。
        if (options.MaxInboundMessageBytes < 1024)
            errors.Add("MaxInboundMessageBytes must be at least 1024 bytes");
    }
}
