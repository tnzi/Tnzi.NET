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
    /// <summary>claude 对 MCP server 的 <c>timeout</c> 取的下限：更小的值被抬到它。</summary>
    private static readonly TimeSpan MinimumEffectiveClaudeToolCallTimeout = TimeSpan.FromSeconds(60);

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

        if (options.ProbeInterval <= TimeSpan.Zero)
            errors.Add("AI:Cli:ProbeInterval must be positive.");

        if (options.IdleWatchdog <= TimeSpan.Zero)
            errors.Add("AI:Cli:IdleWatchdog must be positive; set a large value rather than zero to effectively disable it.");

        // 零或负数时每一次工具调用都在下一个看门狗刻度上被判超时。
        if (options.ToolWatchdog <= TimeSpan.Zero)
            errors.Add("AI:Cli:ToolWatchdog must be positive; set a large value rather than zero to effectively disable it.");

        if (options.HandshakeTimeout <= TimeSpan.Zero)
            errors.Add("AI:Cli:HandshakeTimeout must be positive.");

        if (options.HardTimeout is { } hard && hard <= TimeSpan.Zero)
            errors.Add("AI:Cli:HardTimeout must be positive when set; use null to disable it.");

        if (options.TerminateGrace < TimeSpan.Zero)
            errors.Add("AI:Cli:TerminateGrace cannot be negative.");

        ValidateWorkspacesRoot(options, CliWorkspaceLayout.DefaultWorkspacesRoot, errors);
        ValidateCustomProviders(options, errors);
        ValidateProviderAuthAndIsolation(options, errors);
        ValidateWriteBack(options.WriteBack, options.ToolWatchdog, errors);

        if (options.ToolResults is null)
        {
            errors.Add("AI:Cli:ToolResults must not be null.");
        }
        else
        {
            if (options.ToolResults.MaxImageBytes <= 0)
                errors.Add("AI:Cli:ToolResults:MaxImageBytes must be greater than zero.");
            if (options.ToolResults.MaxImagesPerResult < 0)
                errors.Add("AI:Cli:ToolResults:MaxImagesPerResult cannot be negative; use 0 to store no tool images.");
        }
        ValidateGc(options.Gc, errors);
        ValidateReattach(options.Reattach, errors);
        ValidateApproval(options.Approval, errors);
        ValidateActivation(options, errors);
        ValidateAssignedWorkDirectoryRoots(options, errors);
    }

    private static void ValidateActivation(CliAgentOptions options, List<string> errors)
    {
        if (options.Activation is not { } activation)
        {
            errors.Add("AI:Cli:Activation must not be null.");
            return;
        }

        if (activation.Timeout <= TimeSpan.Zero)
            errors.Add("AI:Cli:Activation:Timeout must be positive.");

        if (activation.RetryInterval <= TimeSpan.Zero)
            errors.Add("AI:Cli:Activation:RetryInterval must be positive.");

        // 每个副本每轮刷一次 LastSeenAt：在线判定的窗口比两轮还短，连着的目标也会在两轮之间被判离线、被反复唤起。
        if (activation.OnlineTtl <= options.PollInterval * 2)
            errors.Add("AI:Cli:Activation:OnlineTtl must be longer than twice AI:Cli:PollInterval, or a connected target is seen as offline between two polls.");
    }

    /// <summary>
    /// 本机按运行指定工作目录的根：必须是绝对路径，且不能与受管工作区根重叠（回收器会把那里的子目录当成运行目录删掉）。
    /// </summary>
    private static void ValidateAssignedWorkDirectoryRoots(CliAgentOptions options, List<string> errors)
    {
        if (options.AssignedWorkDirectoryRoots.Count == 0)
        {
            return;
        }

        var workspacesRoot = CliWorkspaceLayout.ResolveWorkspacesRoot(options);
        foreach (var root in options.AssignedWorkDirectoryRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            {
                errors.Add($"AI:Cli:AssignedWorkDirectoryRoots['{root}'] must be an absolute path.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(workspacesRoot))
            {
                continue;
            }

            var paths = CliHostPaths.Native;
            if (paths.AreSame(root, workspacesRoot) || paths.IsUnder(root, workspacesRoot) || paths.IsUnder(workspacesRoot, root))
                errors.Add($"AI:Cli:AssignedWorkDirectoryRoots['{root}'] overlaps AI:Cli:WorkspacesRoot; the workspace GC would delete directories under it.");
        }
    }

    private static void ValidateReattach(CliReattachOptions? reattach, List<string> errors)
    {
        if (reattach is null)
        {
            errors.Add("AI:Cli:Reattach must not be null.");
            return;
        }

        if (reattach.Timeout <= TimeSpan.Zero)
            errors.Add("AI:Cli:Reattach:Timeout must be positive.");

        if (reattach.RetryInterval <= TimeSpan.Zero)
            errors.Add("AI:Cli:Reattach:RetryInterval must be positive.");

        if (reattach.ReplayOrderingWait <= TimeSpan.Zero)
            errors.Add("AI:Cli:Reattach:ReplayOrderingWait must be positive.");
    }

    private static void ValidateApproval(CliApprovalOptions? approval, List<string> errors)
    {
        if (approval is null)
        {
            errors.Add("AI:Cli:Approval must not be null.");
            return;
        }

        if (approval.Timeout <= TimeSpan.Zero)
            errors.Add("AI:Cli:Approval:Timeout must be positive.");

        if (approval.PollInterval <= TimeSpan.Zero)
            errors.Add("AI:Cli:Approval:PollInterval must be positive.");
        else if (approval.PollInterval > approval.Timeout)
            errors.Add("AI:Cli:Approval:PollInterval must not be longer than AI:Cli:Approval:Timeout.");
    }

    private static void ValidateWriteBack(CliWriteBackOptions writeBack, TimeSpan toolWatchdog, List<string> errors)
    {
        if (!writeBack.Enabled)
        {
            return;
        }

        // 回写面是安全决定：没写就是没决定，而「没决定 = 全部」是最危险的那种缺省。
        if (writeBack.AllowedTools.Count == 0 || writeBack.AllowedTools.All(string.IsNullOrWhiteSpace))
            errors.Add("AI:Cli:WriteBack:AllowedTools must list the MCP tools a run-scoped credential may call (use [\"*\"] to allow every exposed tool) when WriteBack is enabled.");

        // 没有端点，每次运行都只记一条 Warning、agent 手里没有回写通道，而开关看起来是开着的。
        if (!Uri.TryCreate(writeBack.McpEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            errors.Add("AI:Cli:WriteBack:McpEndpoint must be the absolute http(s) URL of the framework MCP server endpoint as the agent can reach it when WriteBack is enabled.");

        // 零或负数时签发出来的凭据当场就是过期的，每一次回写调用都被拒。
        if (writeBack.TokenLifetime <= TimeSpan.Zero)
            errors.Add("AI:Cli:WriteBack:TokenLifetime must be positive.");

        if (writeBack.ToolCallTimeout is { } toolCallTimeout)
        {
            // 零或负数不是一个上限；claude 也不认它，配了等于没配，而配置看上去是改过的。
            if (toolCallTimeout <= TimeSpan.Zero)
                errors.Add("AI:Cli:WriteBack:ToolCallTimeout must be positive when set; use null to keep the CLI's own default.");
            // 工具看门狗从工具开始起算、到点判整次运行超时：比它长的单次调用上限永远轮不到生效。
            else if (toolCallTimeout >= toolWatchdog)
                errors.Add("AI:Cli:WriteBack:ToolCallTimeout must be shorter than AI:Cli:ToolWatchdog; otherwise the tool watchdog ends the whole run before a call reaches its limit.");
        }
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
        foreach (var (key, provider, configVariable, tokenVariable) in EnabledProviderEntries(options))
        {
            if (provider.UserConfigIsolation is CliUserConfigIsolation.IsolatedConfigDirectory or CliUserConfigIsolation.PerWorkspaceConfigDirectory
                    or CliUserConfigIsolation.ProvidedConfigDirectory
                && string.IsNullOrWhiteSpace(configVariable))
                errors.Add($"AI:Cli provider '{key}' cannot relocate its configuration directory, so UserConfigIsolation={provider.UserConfigIsolation} is not supported for it.");

            if (!string.IsNullOrWhiteSpace(provider.ConfigDirectory) && !Path.IsPathFullyQualified(provider.ConfigDirectory))
                errors.Add($"AI:Cli provider '{key}' ConfigDirectory must be an absolute path.");

            if (!string.IsNullOrWhiteSpace(provider.FallbackAuthToken) && string.IsNullOrWhiteSpace(tokenVariable))
                errors.Add($"AI:Cli provider '{key}' does not support FallbackAuthToken (no token environment variable is known for it).");

            // 写错的版本号会让范围门要么什么都拦、要么什么都不拦，而配置看起来是生效的
            if (!string.IsNullOrWhiteSpace(provider.MinimumVersion) && CliVersionSupport.Parse(provider.MinimumVersion) is null)
                errors.Add($"AI:Cli provider '{key}' MinimumVersion '{provider.MinimumVersion}' is not a version number (e.g. 2.1.300).");

            if (!string.IsNullOrWhiteSpace(provider.MaximumVersion) && CliVersionSupport.Parse(provider.MaximumVersion) is null)
                errors.Add($"AI:Cli provider '{key}' MaximumVersion '{provider.MaximumVersion}' is not a version number (e.g. 2.1).");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 都只告警：判断依据在宿主环境里（环境变量有没有设、白名单里的变量是不是凭据）或取决于是哪个 CLI，校验器看不全，
    /// 拒绝启动会误伤配对了的部署。
    /// </remarks>
    protected override void CollectWarnings(CliAgentOptions options, List<string> warnings)
    {
        if (!options.Enabled)
        {
            return;
        }

        // claude 把低于 60 秒的值抬到 60 秒（实测 2.1.302），配了也缩短不了；别的 stream-json CLI 未必如此，所以不拒绝。
        if (options.WriteBack is { Enabled: true, ToolCallTimeout: { } toolCallTimeout }
            && toolCallTimeout > TimeSpan.Zero && toolCallTimeout < MinimumEffectiveClaudeToolCallTimeout)
            warnings.Add("AI:Cli:WriteBack:ToolCallTimeout is below 60 seconds; claude raises such values to 60 seconds, so it cannot shorten write-back calls there.");

        foreach (var (key, provider, _, tokenVariable) in EnabledProviderEntries(options))
        {
            // 每工作区配置目录里永远没有存储的登录：没有兜底令牌、白名单里也看不出有凭据时，每次运行都会在认证上失败
            if (provider.UserConfigIsolation == CliUserConfigIsolation.PerWorkspaceConfigDirectory
                && string.IsNullOrWhiteSpace(provider.FallbackAuthToken)
                && !options.InheritAllHostEnvironment
                && !options.EnvironmentWhitelist.Any(name => LooksLikeCredential(name, tokenVariable)))
                warnings.Add($"AI:Cli provider '{key}' uses UserConfigIsolation=PerWorkspaceConfigDirectory, which never contains a stored sign-in. "
                             + "Set FallbackAuthToken or pass a token or API key through AI:Cli:EnvironmentWhitelist, otherwise every run fails to authenticate.");

            if (!string.IsNullOrWhiteSpace(provider.ConfigDirectory)
                && provider.UserConfigIsolation != CliUserConfigIsolation.IsolatedConfigDirectory)
                warnings.Add($"AI:Cli provider '{key}' sets ConfigDirectory, which only applies to UserConfigIsolation=IsolatedConfigDirectory; it is ignored with {provider.UserConfigIsolation}.");
        }
    }

    /// <summary>白名单里的这个变量名看上去是凭据：provider 声明的令牌变量，或以 <c>_API_KEY</c> / <c>_TOKEN</c> 结尾。</summary>
    private static bool LooksLikeCredential(string name, string? tokenVariable)
        => (!string.IsNullOrWhiteSpace(tokenVariable) && string.Equals(name, tokenVariable, StringComparison.OrdinalIgnoreCase))
           || name.EndsWith("_API_KEY", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith("_TOKEN", StringComparison.OrdinalIgnoreCase);

    /// <summary>启用的 provider 及其能力：内置项取描述表里的能力，同名的自定义 provider 覆盖内置项。</summary>
    private static IEnumerable<(string Key, CliProviderOptions Options, string? ConfigVariable, string? TokenVariable)> EnabledProviderEntries(
        CliAgentOptions options)
        => options.Providers
            .Where(p => CliBuiltInProviders.All.ContainsKey(p.Key))
            .Select(p => (p.Key, Options: p.Value,
                ConfigVariable: CliBuiltInProviders.All[p.Key].ConfigDirectoryEnvironmentVariable,
                TokenVariable: CliBuiltInProviders.All[p.Key].AuthTokenEnvironmentVariable))
            .Where(e => !options.CustomProviders.Any(c => string.Equals(c.Key, e.Key, StringComparison.OrdinalIgnoreCase)))
            .Concat(options.CustomProviders
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .Select(c => (c.Key, Options: (CliProviderOptions)c,
                    ConfigVariable: c.ConfigDirectoryEnvironmentVariable,
                    TokenVariable: c.AuthTokenEnvironmentVariable)))
            .Where(e => e.Options.Enabled);

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

        // 零或负数时每一轮回收都把每个终态工作区的可再生目录删一遍。
        if (gc.ArtifactTtl <= TimeSpan.Zero)
            errors.Add("AI:Cli:Gc:ArtifactTtl must be positive.");

        // 一个含路径分隔符的条目会让「只删可再生目录」变成「按相对路径删任意东西」。
        // 运行期已经静默丢弃它们，这里让部署方在启动时就知道自己写错了。
        foreach (var pattern in gc.ArtifactPatterns)
        {
            if (pattern.Contains('/') || pattern.Contains('\\'))
                errors.Add($"AI:Cli:Gc:ArtifactPatterns['{pattern}'] must be a bare directory name; path separators are not allowed.");
        }
    }
}
