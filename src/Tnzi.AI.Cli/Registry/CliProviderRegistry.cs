namespace Tnzi.AI.Cli.Registry;

/// <summary>
/// 合并内置描述表与 <c>AI:Cli</c> 配置，得到本部署的有效 provider 视图。
/// </summary>
/// <remarks>
/// 合并优先级（后者覆盖前者）：内置表 → <c>Providers[key]</c> 覆盖 → <c>CustomProviders</c>。
/// <c>CustomProviders</c> 排在最后，因此它既能新增 provider，也能整体替换一个内置项
/// （例如同一个 CLI 换了可执行名或子命令）。
/// <para>
/// 用 <c>IOptionsMonitor</c> 而不是 <c>IOptions</c>：provider 启停与可执行路径属于
/// 运维会改的配置，改完不该要求重启进程。
/// </para>
/// </remarks>
public class CliProviderRegistry : ICliProviderRegistry
{
    private readonly IOptionsMonitor<CliAgentOptions> _options;

    /// <summary>初始化 provider 注册表。</summary>
    public CliProviderRegistry(IOptionsMonitor<CliAgentOptions> options)
    {
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public IReadOnlyList<CliProviderDescriptor> GetAll() => Build(_options.CurrentValue);

    /// <inheritdoc />
    public IReadOnlyList<CliProviderDescriptor> GetEnabled()
        => Build(_options.CurrentValue).Where(d => d.Enabled).ToList();

    /// <inheritdoc />
    public CliProviderDescriptor? Find(string providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return null;
        }

        return Build(_options.CurrentValue)
            .FirstOrDefault(d => string.Equals(d.Key, providerKey, StringComparison.OrdinalIgnoreCase));
    }

    private static List<CliProviderDescriptor> Build(CliAgentOptions options)
    {
        var merged = new Dictionary<string, CliProviderDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, descriptor) in CliBuiltInProviders.All)
        {
            merged[key] = ApplyOverride(descriptor, options.Providers.GetValueOrDefault(key));
        }

        foreach (var custom in options.CustomProviders)
        {
            if (string.IsNullOrWhiteSpace(custom.Key) || string.IsNullOrWhiteSpace(custom.DefaultExecutable))
            {
                // 校验器已在启动期报过错；运行期静默跳过残缺条目，不让一条错配置
                // 连带弄坏其余 provider 的解析。
                continue;
            }

            merged[custom.Key] = new CliProviderDescriptor
            {
                Key = custom.Key,
                DisplayName = string.IsNullOrWhiteSpace(custom.DisplayName) ? custom.Key : custom.DisplayName,
                Protocol = custom.Protocol,
                DefaultExecutable = custom.DefaultExecutable,
                LaunchArgs = custom.LaunchArgs.ToList(),
                BriefFileName = custom.BriefFileName,
                SkillsRelativePath = custom.SkillsRelativePath,
                LaunchHeader = custom.LaunchHeader,
                // 自定义 provider 一律 fail-closed：框架没验证过它能否区分 resume 被拒，
                // 于是「分不清」，于是不做 fresh-session 重试。
                ResumeRejectionDetectable = false,
                RequiresInlineSystemPrompt = string.IsNullOrWhiteSpace(custom.BriefFileName),
                BlockedArgs = ProtocolBlockedArgs(custom.Protocol),
                Enabled = custom.Enabled,
                ExecutablePathOverride = custom.ExecutablePath,
                DefaultModel = custom.DefaultModel,
                ExtraArgs = custom.ExtraArgs.ToList()
            };
        }

        return [.. merged.Values];
    }

    private static CliProviderDescriptor ApplyOverride(CliProviderDescriptor descriptor, CliProviderOptions? overrides)
    {
        if (overrides is null)
        {
            return descriptor;
        }

        return descriptor with
        {
            Enabled = overrides.Enabled,
            ExecutablePathOverride = overrides.ExecutablePath,
            DefaultModel = overrides.DefaultModel,
            ExtraArgs = overrides.ExtraArgs.ToList()
        };
    }

    /// <summary>
    /// 自定义 provider 继承其协议族的被禁参数 —— 那些参数属于**协议契约**，
    /// 不是某个 CLI 的偏好，所以不该让配置方自己重复声明（也就不会漏声明）。
    /// </summary>
    private static IReadOnlyDictionary<string, BlockedArgMode> ProtocolBlockedArgs(CliAgentProtocol protocol)
    {
        var template = CliBuiltInProviders.All.Values.FirstOrDefault(d => d.Protocol == protocol);
        return template?.BlockedArgs ?? new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal);
    }
}
