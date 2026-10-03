namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 一次启动在「协议参数」之外，由部署配置决定的参数与环境变量。
/// </summary>
public sealed record CliLaunchEnvironment
{
    /// <summary>空结果：不加参数、不设变量。</summary>
    public static CliLaunchEnvironment None { get; } = new();

    /// <summary>追加在部署级 ExtraArgs 之前的参数（隔离档位带来的）。</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>显式设置给子进程的环境变量（配置目录重定向、兜底令牌）。</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否注入了兜底令牌（供日志与测试，不含令牌本身）。</summary>
    public bool UsesFallbackToken { get; init; }
}

/// <summary>
/// 按 provider 的部署配置，算出一次启动要带的隔离参数、配置目录与兜底认证。
/// </summary>
public interface ICliLaunchEnvironmentComposer
{
    /// <summary>
    /// 组合启动环境。
    /// </summary>
    /// <param name="provider">provider 描述（含部署级隔离档位）。</param>
    /// <param name="executablePath">已解析的可执行文件路径，登录状态查询用它。</param>
    /// <param name="workingDirectory">运行的工作目录，登录状态查询也在这里跑。</param>
    /// <param name="options">当前配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="CliProviderConfigurationException">配置要求了该 provider 不支持的能力。</exception>
    Task<CliLaunchEnvironment> ComposeAsync(
        CliProviderDescriptor provider, string executablePath, string workingDirectory,
        CliAgentOptions options, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICliLaunchEnvironmentComposer" />
/// <remarks>
/// <para>
/// ★ <b>兜底令牌只在查不到登录态时注入。</b>claude 的认证优先级里，环境变量中的 OAuth 令牌高于本机登录，
/// 无条件注入会顶掉部署方本来想用的那份登录。所以先用运行将要使用的同一份环境
/// （同一个配置目录、同一份白名单）问一次「登录了吗」，只有答「没有」或答不出来时才注入。
/// 答不出来按「没有」处理：配置了兜底，就是宁可用它也不要让运行在认证上失败。
/// </para>
/// <para>令牌是机密：只进子进程环境，不进日志、不进运行记录、不进 provider 描述。</para>
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
        CliProviderDescriptor provider, string executablePath, string workingDirectory,
        CliAgentOptions options, CancellationToken cancellationToken)
    {
        Check.NotNull(provider);
        Check.NotNullOrWhiteSpace(executablePath);
        Check.NotNullOrWhiteSpace(workingDirectory);
        Check.NotNull(options);

        var args = new List<string>();
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (provider.UserConfigIsolation)
        {
            case CliUserConfigIsolation.ExcludeUserSettings:
                // 默认档对所有 provider 生效，不支持的 provider 就是不加参数（它本来也没有可排除的东西可言）。
                args.AddRange(provider.ExcludeUserSettingsArgs);
                break;

            case CliUserConfigIsolation.IsolatedConfigDirectory:
                if (string.IsNullOrWhiteSpace(provider.ConfigDirectoryEnvironmentVariable))
                {
                    throw new CliProviderConfigurationException(provider.Key,
                        $"Provider '{provider.Key}' cannot relocate its configuration directory, so UserConfigIsolation=IsolatedConfigDirectory cannot be honoured.");
                }

                var configDirectory = CliWorkspaceLayout.ResolveConfigDirectory(provider, options);
                Directory.CreateDirectory(configDirectory);
                environment[provider.ConfigDirectoryEnvironmentVariable] = configDirectory;
                break;
        }

        var token = options.FindProviderOptions(provider.Key)?.FallbackAuthToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new CliLaunchEnvironment { Args = args, Environment = environment };
        }

        if (string.IsNullOrWhiteSpace(provider.AuthTokenEnvironmentVariable))
        {
            throw new CliProviderConfigurationException(provider.Key,
                $"Provider '{provider.Key}' has a FallbackAuthToken configured but declares no environment variable to deliver it through.");
        }

        var signedIn = await IsSignedInAsync(provider, executablePath, workingDirectory, environment, options, cancellationToken);
        if (signedIn == true)
        {
            return new CliLaunchEnvironment { Args = args, Environment = environment };
        }

        _logger.LogInformation(
            "Provider {Provider} has no usable sign-in on this host (status={Status}); using the configured fallback token",
            provider.Key, signedIn is null ? "unknown" : "signed-out");

        environment[provider.AuthTokenEnvironmentVariable] = token;
        return new CliLaunchEnvironment { Args = args, Environment = environment, UsesFallbackToken = true };
    }

    private async Task<bool?> IsSignedInAsync(
        CliProviderDescriptor provider, string executablePath, string workingDirectory,
        IReadOnlyDictionary<string, string> environment, CliAgentOptions options, CancellationToken cancellationToken)
    {
        if (provider.AuthStatusArgs.Count == 0)
        {
            return null;
        }

        return await _authStatusProbe.IsSignedInAsync(new CliProcessSpec
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
