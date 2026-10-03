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

        ValidateWorkspacesRoot(options, CliWorkspaceLayout.DefaultWorkspacesRoot, errors);
        ValidateCustomProviders(options, errors);
        ValidateProviderAuthAndIsolation(options, errors);
        ValidateWriteBack(options.WriteBack, errors);
        ValidateGc(options.Gc, errors);
    }

    private static void ValidateWriteBack(CliWriteBackOptions writeBack, List<string> errors)
    {
        if (!writeBack.Enabled)
        {
            return;
        }

        // 回写面是安全决定：没写就是没决定，而「没决定 = 全部」是最危险的那种缺省。
        if (writeBack.AllowedTools.Count == 0 || writeBack.AllowedTools.All(string.IsNullOrWhiteSpace))
            errors.Add("AI:Cli:WriteBack:AllowedTools must list the MCP tools a run-scoped credential may call (use [\"*\"] to allow every exposed tool) when WriteBack is enabled.");
    }

    /// <summary>
    /// 没配 <see cref="CliAgentOptions.WorkspacesRoot"/> 且宿主解析不出默认根目录时拒绝启动：
    /// 不退回相对路径，那会把每次运行的工作区写进进程当前目录（通常就是部署目录）。
    /// </summary>
    internal static void ValidateWorkspacesRoot(CliAgentOptions options, string defaultWorkspacesRoot, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(options.WorkspacesRoot) || !string.IsNullOrWhiteSpace(defaultWorkspacesRoot))
            return;

        errors.Add(
            "AI:Cli:WorkspacesRoot is not set and the default location could not be resolved because this host exposes no per-user " +
            "data directory (LocalApplicationData: HOME / XDG_DATA_HOME on Linux and macOS, the user profile on Windows). " +
            "Set AI:Cli:WorkspacesRoot to an absolute path outside the application directory.");
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

    /// <summary>
    /// 显式选了某个 provider 做不到的隔离或兜底认证时拒绝启动。
    /// </summary>
    /// <remarks>
    /// 默认档 <see cref="CliUserConfigIsolation.ExcludeUserSettings"/> 对不支持的 provider 只是不加参数，不报错 ——
    /// 它是缺省值，报错等于让每个用 ACP CLI 的部署都得先去关掉一个自己从没开过的东西。
    /// 专用配置目录与兜底令牌则是部署方主动要的，做不到就该当场知道，而不是以「未隔离 / 没兜底」悄悄跑。
    /// </remarks>
    private static void ValidateProviderAuthAndIsolation(CliAgentOptions options, List<string> errors)
    {
        var entries = options.Providers
            .Where(p => CliBuiltInProviders.All.ContainsKey(p.Key))
            .Select(p => (p.Key, Options: p.Value,
                ConfigVariable: CliBuiltInProviders.All[p.Key].ConfigDirectoryEnvironmentVariable,
                TokenVariable: CliBuiltInProviders.All[p.Key].AuthTokenEnvironmentVariable))
            .Where(e => !options.CustomProviders.Any(c => string.Equals(c.Key, e.Key, StringComparison.OrdinalIgnoreCase)))
            .Concat(options.CustomProviders
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .Select(c => (c.Key, Options: (CliProviderOptions)c,
                    ConfigVariable: c.ConfigDirectoryEnvironmentVariable,
                    TokenVariable: c.AuthTokenEnvironmentVariable)));

        foreach (var (key, provider, configVariable, tokenVariable) in entries)
        {
            if (!provider.Enabled)
            {
                continue;
            }

            if (provider.UserConfigIsolation == CliUserConfigIsolation.IsolatedConfigDirectory
                && string.IsNullOrWhiteSpace(configVariable))
                errors.Add($"AI:Cli provider '{key}' cannot relocate its configuration directory, so UserConfigIsolation=IsolatedConfigDirectory is not supported for it.");

            if (!string.IsNullOrWhiteSpace(provider.ConfigDirectory) && !Path.IsPathFullyQualified(provider.ConfigDirectory))
                errors.Add($"AI:Cli provider '{key}' ConfigDirectory must be an absolute path.");

            if (!string.IsNullOrWhiteSpace(provider.FallbackAuthToken) && string.IsNullOrWhiteSpace(tokenVariable))
                errors.Add($"AI:Cli provider '{key}' does not support FallbackAuthToken (no token environment variable is known for it).");
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
