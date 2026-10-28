namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 一次启动在「协议参数」之外的参数与环境变量：由部署配置（隔离档位、兜底令牌）与这次运行的启动档案决定。
/// </summary>
public sealed record CliLaunchEnvironment
{
    /// <summary>空结果：不加参数、不设变量。</summary>
    public static CliLaunchEnvironment None { get; } = new();

    /// <summary>
    /// 追加在部署级 ExtraArgs 之前的参数（隔离档位、启动档案、用户目录模式下的追加系统提示与附加目录带来的）。不经被禁参数过滤。
    /// </summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>
    /// 这次运行由启动档案接管的开关：部署级 ExtraArgs 与绑定的自定义参数里的同名开关被丢弃，不会排在后面把档案的值盖掉。
    /// </summary>
    public IReadOnlyDictionary<string, BlockedArgMode> OwnedArgs { get; init; }
        = new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal);

    /// <summary>显式设置给子进程的环境变量（档案给的、配置目录重定向、项目目录名、兜底令牌）。</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否注入了兜底令牌（供日志与测试，不含令牌本身）。</summary>
    public bool UsesFallbackToken { get; init; }
}

/// <summary>
/// 按 provider 的部署配置与这次运行的启动档案，算出一次启动要带的参数、配置目录、环境变量与兜底认证。
/// </summary>
public interface ICliLaunchEnvironmentComposer
{
    /// <summary>
    /// 组合启动环境。
    /// </summary>
    /// <param name="provider">provider 描述（含部署级隔离档位）。</param>
    /// <param name="executablePath">已解析的可执行文件路径（目标上的），登录状态查询用它。</param>
    /// <param name="workspace">这次运行的工作区：登录状态查询在它的工作目录里跑，每工作区配置目录放在它的运行根下。</param>
    /// <param name="target">这次运行的执行目标：配置目录建在它上面，登录状态在它上面查。</param>
    /// <param name="options">当前配置。</param>
    /// <param name="profile">这次运行的启动档案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="CliProviderConfigurationException">
    /// 配置或启动档案要求了该 provider 不支持的能力，或这个工作区用不了所选的隔离档位。
    /// </exception>
    Task<CliLaunchEnvironment> ComposeAsync(
        CliProviderDescriptor provider, string executablePath, CliWorkspace workspace, ICliHostTarget target,
        CliAgentOptions options, CliLaunchProfile profile, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICliLaunchEnvironmentComposer" />
/// <remarks>
/// <para>
/// ★ <b>兜底令牌只在查不到登录态时注入。</b>claude 的认证优先级里，环境变量中的 OAuth 令牌高于本机登录，
/// 无条件注入会顶掉部署方本来想用的那份登录。所以先用运行将要使用的同一份环境
/// （同一个配置目录、同一份白名单、档案给的环境变量）问一次「登录了吗」，只有答「没有」或答不出来时才注入。
/// 答不出来按「没有」处理：配置了兜底，就是宁可用它也不要让运行在认证上失败。
/// </para>
/// <para>令牌是机密：只进子进程环境，不进日志、不进运行记录、不进 provider 描述。</para>
/// <para>
/// 启动档案要了 provider 没声明的能力（例如对一个没有 <c>--tools</c> 的 CLI 限定内置工具）时抛
/// <see cref="CliProviderConfigurationException"/>，运行以 <c>LaunchFailed</c> 结束：档案里多数项是在收紧，静默不带它跑就是放宽。
/// </para>
/// </remarks>
public class CliLaunchEnvironmentComposer : ICliLaunchEnvironmentComposer
{
    private readonly ICliAuthStatusProbe _authStatusProbe;
    private readonly ILogger<CliLaunchEnvironmentComposer> _logger;

    /// <summary>初始化启动环境组合器。</summary>
    public CliLaunchEnvironmentComposer(ICliAuthStatusProbe authStatusProbe, ILogger<CliLaunchEnvironmentComposer> logger)
    {
        _authStatusProbe = Check.NotNull(authStatusProbe);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<CliLaunchEnvironment> ComposeAsync(
        CliProviderDescriptor provider, string executablePath, CliWorkspace workspace, ICliHostTarget target,
        CliAgentOptions options, CliLaunchProfile profile, CancellationToken cancellationToken)
    {
        Check.NotNull(provider);
        Check.NotNullOrWhiteSpace(executablePath);
        Check.NotNull(workspace);
        Check.NotNull(target);
        Check.NotNull(options);
        Check.NotNull(profile);

        var workingDirectory = workspace.WorkDirectory;
        var args = new List<string>();
        var owned = new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal);

        // 档案给的变量先放，框架自己的（配置目录、项目目录名、令牌）后写、同名时以框架的为准
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in profile.Environment)
        {
            environment[name] = value;
        }

        switch (provider.UserConfigIsolation)
        {
            case CliUserConfigIsolation.ExcludeUserSettings:
                // 默认档对所有 provider 生效，不支持的 provider 就是不加参数（它本来也没有可排除的东西可言）。
                // 档案自己点了设置来源时以档案为准
                if (profile.SettingSources is null)
                {
                    args.AddRange(provider.ExcludeUserSettingsArgs);
                }

                break;

            case CliUserConfigIsolation.IsolatedConfigDirectory:
                await RedirectConfigDirectoryAsync(
                    provider, target, CliWorkspaceLayout.ResolveConfigDirectory(provider, target), environment, cancellationToken);
                break;

            case CliUserConfigIsolation.PerWorkspaceConfigDirectory:
                // 用户提供的工作目录按运行分运行根：每一轮一个新配置目录，前一轮的会话永远找不到，
                // 而工作区清理又只删产物与日志、不删运行根，会话记录也不会随之消失。两个承诺都兑现不了，当场拒绝。
                if (workspace.WorkDirectoryIsUserOwned)
                {
                    throw new CliProviderConfigurationException(provider.Key,
                        $"UserConfigIsolation=PerWorkspaceConfigDirectory for provider '{provider.Key}' needs a managed work directory (PerThread or PerRun); this agent is bound to a user-provided work directory. Use ProvidedConfigDirectory instead.");
                }

                await RedirectConfigDirectoryAsync(
                    provider, target,
                    CliWorkspaceLayout.ResolveWorkspaceConfigDirectory(workspace.RootDirectory, provider, target.FileSystem.Paths),
                    environment, cancellationToken);
                break;

            case CliUserConfigIsolation.ProvidedConfigDirectory:
                await UseProvidedConfigDirectoryAsync(provider, target, profile, environment, cancellationToken);
                break;
        }

        AppendProfileArgs(provider, profile, workspace, args, owned);

        var token = profile.UseFallbackAuthToken ? options.FindProviderOptions(provider.Key)?.FallbackAuthToken : null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new CliLaunchEnvironment { Args = args, OwnedArgs = owned, Environment = environment };
        }

        if (string.IsNullOrWhiteSpace(provider.AuthTokenEnvironmentVariable))
        {
            throw new CliProviderConfigurationException(provider.Key,
                $"Provider '{provider.Key}' has a FallbackAuthToken configured but declares no environment variable to deliver it through.");
        }

        var signedIn = await IsSignedInAsync(provider, target, executablePath, workingDirectory, environment, options, cancellationToken);
        if (signedIn == true)
        {
            return new CliLaunchEnvironment { Args = args, OwnedArgs = owned, Environment = environment };
        }

        _logger.LogInformation(
            "Provider {Provider} has no usable sign-in on {Host} (status={Status}); using the configured fallback token",
            provider.Key, target.Name, signedIn is null ? "unknown" : "signed-out");

        environment[provider.AuthTokenEnvironmentVariable] = token;
        return new CliLaunchEnvironment { Args = args, OwnedArgs = owned, Environment = environment, UsesFallbackToken = true };
    }

    /// <summary>
    /// 档案给的设置来源、附加设置、内置工具与附加目录，以及用户目录模式下的追加系统提示与技能目录。
    /// </summary>
    /// <remarks>
    /// 档案点了名的开关同时登记为「本次接管」（<paramref name="owned"/>），部署级参数与绑定的自定义参数里的同名开关因此被丢弃。
    /// 附加目录与追加系统提示可以重复给，只追加不接管。
    /// </remarks>
    private static void AppendProfileArgs(
        CliProviderDescriptor provider, CliLaunchProfile profile, CliWorkspace workspace, List<string> args,
        Dictionary<string, BlockedArgMode> owned)
    {
        if (profile.SettingSources is { } sources)
        {
            var flag = Require(provider, provider.SettingSourcesArg, "setting sources", nameof(CliProviderDescriptor.SettingSourcesArg));
            args.Add(flag);
            args.Add(string.Join(',', sources));
            owned[flag] = BlockedArgMode.WithValue;
        }

        if (BuildSettings(provider, profile) is { } settings)
        {
            var flag = Require(provider, provider.SettingsArg, "additional settings", nameof(CliProviderDescriptor.SettingsArg));
            args.Add(flag);
            args.Add(settings);
            owned[flag] = BlockedArgMode.WithValue;
        }

        if (profile.BuiltInTools is { } tools)
        {
            var flag = Require(provider, provider.BuiltInToolsArg, "a built-in tool selection", nameof(CliProviderDescriptor.BuiltInToolsArg));
            args.Add(flag);
            // 空串 = 一个内置工具都不开（claude 的约定），不是「没给」
            args.Add(string.Join(',', tools));
            owned[flag] = BlockedArgMode.WithValue;
        }

        string[] directories = workspace.ContextDirectory is { } contextDirectory
            ? [.. profile.AdditionalDirectories, contextDirectory]
            : [.. profile.AdditionalDirectories];
        if (directories.Length > 0)
        {
            var flag = Require(provider, provider.AddDirectoryArg, "additional directories", nameof(CliProviderDescriptor.AddDirectoryArg));
            foreach (var directory in directories)
            {
                args.Add(flag);
                args.Add(directory);
            }
        }

        // 布置器只在 provider 声明了这个参数时才写这个文件
        if (workspace.SystemPromptFilePath is { } systemPrompt && !string.IsNullOrWhiteSpace(provider.AppendSystemPromptFileArg))
        {
            args.Add(provider.AppendSystemPromptFileArg);
            args.Add(systemPrompt);
        }
    }

    /// <summary>
    /// 附加设置的 JSON：档案给的对象，加上会话记录保留天数。两者都没给时为 null。
    /// </summary>
    private static string? BuildSettings(CliProviderDescriptor provider, CliLaunchProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SettingsJson) && profile.SessionRetentionDays is null)
        {
            return null;
        }

        JsonObject settings;
        try
        {
            settings = string.IsNullOrWhiteSpace(profile.SettingsJson)
                ? new JsonObject()
                : JsonNode.Parse(profile.SettingsJson) as JsonObject
                  ?? throw new CliProviderConfigurationException(provider.Key, "The launch profile's SettingsJson must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new CliProviderConfigurationException(provider.Key, $"The launch profile's SettingsJson is not valid JSON: {ex.Message}");
        }

        if (profile.SessionRetentionDays is { } days)
        {
            if (days <= 0)
            {
                throw new CliProviderConfigurationException(provider.Key, "The launch profile's SessionRetentionDays must be positive.");
            }

            var key = Require(provider, provider.SessionRetentionSettingName, "a session retention period",
                nameof(CliProviderDescriptor.SessionRetentionSettingName));
            settings[key] = days;
        }

        return settings.ToJsonString();
    }

    /// <summary>provider 声明了这项能力就返回它的参数名，没声明就拒绝这一轮。</summary>
    private static string Require(CliProviderDescriptor provider, string? declared, string capability, string field)
        => string.IsNullOrWhiteSpace(declared)
            ? throw new CliProviderConfigurationException(provider.Key,
                $"The run's launch profile asks for {capability}, which provider '{provider.Key}' does not support ({field} is not declared).")
            : declared;

    /// <summary>
    /// <see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/>：配置目录与项目目录名取自这次运行的启动档案。
    /// </summary>
    private static async Task UseProvidedConfigDirectoryAsync(
        CliProviderDescriptor provider, ICliHostTarget target, CliLaunchProfile profile, Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.ConfigDirectory))
        {
            throw new CliProviderConfigurationException(provider.Key,
                $"UserConfigIsolation=ProvidedConfigDirectory for provider '{provider.Key}' needs a configuration directory from the run's launch profile, and none was given.");
        }

        await RedirectConfigDirectoryAsync(provider, target, profile.ConfigDirectory, environment, cancellationToken);

        if (string.IsNullOrWhiteSpace(profile.ProjectDirectoryName))
        {
            return;
        }

        var variable = Require(provider, provider.ProjectDirectoryNameEnvironmentVariable, "a fixed project directory name",
            nameof(CliProviderDescriptor.ProjectDirectoryNameEnvironmentVariable));
        environment[variable] = profile.ProjectDirectoryName;
    }

    /// <summary>把 provider 的配置目录指向 <paramref name="configDirectory"/>（在执行目标上，不存在就建）。</summary>
    private static async Task RedirectConfigDirectoryAsync(
        CliProviderDescriptor provider, ICliHostTarget target, string configDirectory, Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider.ConfigDirectoryEnvironmentVariable))
        {
            throw new CliProviderConfigurationException(provider.Key,
                $"Provider '{provider.Key}' cannot relocate its configuration directory, so UserConfigIsolation={provider.UserConfigIsolation} cannot be honoured.");
        }

        await target.FileSystem.CreateDirectoryAsync(configDirectory, cancellationToken);
        environment[provider.ConfigDirectoryEnvironmentVariable] = configDirectory;
    }

    private async Task<bool?> IsSignedInAsync(
        CliProviderDescriptor provider, ICliProcessHost host, string executablePath, string workingDirectory,
        IReadOnlyDictionary<string, string> environment, CliAgentOptions options, CancellationToken cancellationToken)
    {
        if (provider.AuthStatusArgs.Count == 0)
        {
            return null;
        }

        return await _authStatusProbe.IsSignedInAsync(host, new CliProcessSpec
        {
            ExecutablePath = executablePath,
            Arguments = provider.AuthStatusArgs,
            WorkingDirectory = workingDirectory,
            Environment = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase),
            InheritAllHostEnvironment = options.InheritAllHostEnvironment,
            EnvironmentWhitelist = options.EnvironmentWhitelist
        }, cancellationToken);
    }
}
