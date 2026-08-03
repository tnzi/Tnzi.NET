namespace Tnzi.AI.Cli.Options;

/// <summary>
/// 外部 CLI agent 配置校验。
/// </summary>
/// <remarks>
/// <b><see cref="CliAgentOptions.Enabled"/> = false 时跳过所有检查。</b>
/// 一个被关掉的可选模块不该有能力阻塞应用启动 —— 同仓的 MCP 子模块踩过这个坑：
/// 配置节留空的部署因为验证器无条件跑而起不来，而它们本来就没打算用那个功能。
/// </remarks>
public class CliAgentOptionsValidator : OptionsValidatorBase<CliAgentOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(CliAgentOptions options, List<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (options.MaxConcurrentRuns <= 0)
            errors.Add("AI:Cli:MaxConcurrentRuns must be greater than zero when the module is enabled.");

        if (options.PollInterval <= TimeSpan.Zero)
            errors.Add("AI:Cli:PollInterval must be positive.");

        if (options.LeaseDuration <= TimeSpan.Zero)
            errors.Add("AI:Cli:LeaseDuration must be positive.");

        // 租约必须显著长于轮询间隔，否则续期赶不上过期，运行中的任务会被自己的回收器抢走。
        if (options.LeaseDuration <= options.PollInterval * 2)
            errors.Add("AI:Cli:LeaseDuration must be at least twice AI:Cli:PollInterval so lease renewal can outrun expiry.");

        if (options.IdleWatchdog <= TimeSpan.Zero)
            errors.Add("AI:Cli:IdleWatchdog must be positive; set a large value rather than zero to effectively disable it.");

        if (options.HandshakeTimeout <= TimeSpan.Zero)
            errors.Add("AI:Cli:HandshakeTimeout must be positive.");

        if (options.HardTimeout is { } hard && hard <= TimeSpan.Zero)
            errors.Add("AI:Cli:HardTimeout must be positive when set; use null to disable it.");

        if (options.TerminateGrace < TimeSpan.Zero)
            errors.Add("AI:Cli:TerminateGrace cannot be negative.");

        ValidateCustomProviders(options, errors);
        ValidateGc(options.Gc, errors);
    }

    private static void ValidateCustomProviders(CliAgentOptions options, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var custom in options.CustomProviders)
        {
            if (string.IsNullOrWhiteSpace(custom.Key))
            {
                errors.Add("AI:Cli:CustomProviders entries must declare a non-empty Key.");
                continue;
            }

            if (!seen.Add(custom.Key))
                errors.Add($"AI:Cli:CustomProviders declares duplicate key '{custom.Key}'.");

            if (string.IsNullOrWhiteSpace(custom.DefaultExecutable))
                errors.Add($"AI:Cli:CustomProviders['{custom.Key}'] must declare DefaultExecutable.");

            // 描述表可以声明任何协议，但只有已实现适配器的协议才能真正跑起来。
            // 在启动期说清楚，好过运行时收到一个 501。
            if (custom.Protocol == CliAgentProtocol.VendorAppServer)
                errors.Add($"AI:Cli:CustomProviders['{custom.Key}'] uses VendorAppServer, which has no adapter implementation in this version.");
        }
    }

    private static void ValidateGc(CliWorkspaceGcOptions gc, List<string> errors)
    {
        if (!gc.Enabled)
        {
            return;
        }

        if (gc.Interval <= TimeSpan.Zero)
            errors.Add("AI:Cli:Gc:Interval must be positive when GC is enabled.");

        if (gc.CompletedTtl <= TimeSpan.Zero)
            errors.Add("AI:Cli:Gc:CompletedTtl must be positive.");

        if (gc.OrphanTtl <= TimeSpan.Zero)
            errors.Add("AI:Cli:Gc:OrphanTtl must be positive.");

        // 一个含路径分隔符的条目会让「只删可再生目录」变成「按相对路径删任意东西」。
        // 运行期已经静默丢弃它们，这里让部署方在启动时就知道自己写错了。
        foreach (var pattern in gc.ArtifactPatterns)
        {
            if (pattern.Contains('/') || pattern.Contains('\\'))
                errors.Add($"AI:Cli:Gc:ArtifactPatterns['{pattern}'] must be a bare directory name; path separators are not allowed.");
        }
    }
}
