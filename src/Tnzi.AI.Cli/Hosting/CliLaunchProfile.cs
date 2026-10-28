namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 一次运行怎样启动 CLI：权限、内置工具、MCP 合并、设置来源与附加设置、附加目录、配置目录与项目目录名。
/// </summary>
/// <remarks>
/// <para>
/// 按运行给出，来源有两处，依次生效：执行目标（<see cref="ICliHostTarget.GetLaunchProfileAsync"/>，决定这个位置的缺省）、
/// 消费方注册的 <see cref="ICliLaunchProfileContributor"/>（按这一次运行调整，例如用户为这一轮选的权限档位）。
/// 部署级配置（<c>AI:Cli:Providers</c> 的 <c>ExtraArgs</c>）对这家 CLI 的每一次运行都生效，装不下「这一轮只开网页搜索、
/// 下一轮在工作区里开全部工具」这样的区别。
/// </para>
/// <para>
/// 档案里点名的每一项都归它管：部署级 <c>ExtraArgs</c> 与绑定的自定义参数里同一个开关被丢弃（日志里有一条 Warning），
/// 不会排在框架给的那个之后把它盖掉。没点名的项保持原样。
/// </para>
/// <para>
/// 各项怎样落到 CLI 上由 provider 描述表声明（<see cref="CliProviderDescriptor"/> 的 <c>*Arg</c> / <c>*EnvironmentVariable</c> 字段）；
/// 档案要了 provider 没声明的能力时，运行以 <c>LaunchFailed</c> 结束并写明缺哪一项，而不是不带它悄悄跑起来：
/// 这些项多数是在收紧（内置工具、设置来源），静默丢掉就是放宽。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Per-run launch settings are new; the approval channel for CliPermissionMode.Ask will build on it")]
public sealed record CliLaunchProfile
{
    /// <summary>
    /// 执行目标没有给出档案时的缺省：全权、CLI 自带的全部内置工具、平台的 MCP 与 CLI 自己能找到的 MCP 合并（同名时平台的生效）。
    /// </summary>
    /// <remarks>远程执行目标在用户自己的环境里跑，用户配的 MCP 就是他要用的，所以不排除它们。</remarks>
    public static CliLaunchProfile Default { get; } = new();

    /// <summary>
    /// 本机宿主的档案：与 <see cref="Default"/> 只差 <see cref="StrictMcpConfig"/>。
    /// </summary>
    /// <remarks>
    /// 本机宿主上 CLI 读到的「自己的 MCP」是起 API 进程那个系统账号的（开发机上就是开发者本人的），不是派出这次运行的用户的。
    /// </remarks>
    public static CliLaunchProfile Local { get; } = new() { StrictMcpConfig = true };

    /// <summary>权限档位。缺省 <see cref="CliPermissionMode.Full"/>。</summary>
    public CliPermissionMode PermissionMode { get; init; } = CliPermissionMode.Full;

    /// <summary>
    /// 这一轮有没有人在等：null = 按调用方入队时给的（<c>CliRunRequestDto.Attended</c>，聊天路径对有登录用户的一轮是 true）。
    /// </summary>
    /// <remarks>
    /// 只对 <see cref="CliPermissionMode.Ask"/> 起作用：true 时权限请求以 <c>PermissionRequest</c> 事件交给使用方批准，
    /// false 时没有人能回答，要权限的操作按只读档直接拒绝。例如定时任务给 false，免得每个请求都挂到超时。
    /// </remarks>
    public bool? Attended { get; init; }

    /// <summary>
    /// 这次运行可用的内置工具（claude：<c>--tools</c>，如 <c>["WebSearch"]</c>）。null = CLI 自带的全部；空列表 = 一个都不开。
    /// </summary>
    public IReadOnlyList<string>? BuiltInTools { get; init; }

    /// <summary>
    /// true = 只用平台给的 MCP 配置（claude：<c>--strict-mcp-config</c>）；false = 与 CLI 自己找到的（用户级、项目级）合并。
    /// </summary>
    /// <remarks>
    /// 实测 claude 2.1 不加 strict 时，<c>--mcp-config</c> 给的条目与用户配的同名时前者生效，后者被静默丢掉，init 事件里看不出冲突：
    /// 要在界面上提示冲突，平台的条目名用一个保留前缀，启动前比对用户配置。
    /// ACP 没有对应的开关：<c>session/new</c> 里的 <c>mcpServers</c> 怎样与 agent 自己的配置合并由 agent 决定，这一项对 ACP 不起作用。
    /// </remarks>
    public bool StrictMcpConfig { get; init; }

    /// <summary>
    /// 从哪些来源读设置（claude：<c>--setting-sources</c>，取值 <c>user</c> / <c>project</c> / <c>local</c>）。
    /// null = 按 provider 的 <c>UserConfigIsolation</c> 档位。
    /// </summary>
    /// <remarks>
    /// ★ 工作目录是用户仓库时要排除 <c>project</c> 与 <c>local</c>：实测 claude 2.1 会照仓库提交的 <c>.claude/settings.json</c>
    /// 里的 <c>env.ANTHROPIC_BASE_URL</c> 把平台的 OAuth 令牌发到那个地址。仓库自己的权限规则要保留时，
    /// 把仓库设置去掉凭据与端点相关的键之后经 <see cref="SettingsJson"/> 交回。
    /// </remarks>
    public IReadOnlyList<string>? SettingSources { get; init; }

    /// <summary>
    /// 附加设置（claude：<c>--settings</c>），一个 JSON 对象。null = 不给。
    /// </summary>
    /// <remarks>以 JSON 文本交给 CLI，不落文件：落在工作目录里会进用户仓库的改动。</remarks>
    public string? SettingsJson { get; init; }

    /// <summary>
    /// 会话记录保留天数（claude：设置项 <c>cleanupPeriodDays</c>，并进 <see cref="SettingsJson"/> 交给 CLI）。null = CLI 的缺省。
    /// </summary>
    /// <remarks>
    /// 删除线程时框架按会话 id 删记录（见 <see cref="ConfigDirectory"/>）；执行目标当时连不上的，记录留到 CLI 自己按这个期限清掉。
    /// </remarks>
    public int? SessionRetentionDays { get; init; }

    /// <summary>附加目录（claude：<c>--add-dir</c>，可重复）：执行目标上的路径，agent 能读写它们，CLI 加载其中的技能。</summary>
    /// <remarks>
    /// 附加目录里的 <c>CLAUDE.md</c> 与 <c>.claude/rules/</c> 只在环境变量 <c>CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1</c>
    /// 时加载（实测 claude 2.1，技能不需要它），经 <see cref="Environment"/> 给出。
    /// </remarks>
    public IReadOnlyList<string> AdditionalDirectories { get; init; } = [];

    /// <summary>
    /// 这次运行额外设置的环境变量（如 <c>CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1</c>、<c>CLAUDE_CODE_DISABLE_AUTO_MEMORY=1</c>）。
    /// </summary>
    /// <remarks>框架自己设置的变量（配置目录、项目目录名、兜底令牌）与它同名时以框架的为准。</remarks>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// <see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/> 时的配置目录（执行目标上的绝对路径，claude：<c>CLAUDE_CONFIG_DIR</c>）。
    /// </summary>
    /// <remarks>
    /// 别的档位不读它。这一档下删除线程时，框架按每一轮的会话 id 删掉这个目录里对应的会话记录
    /// （provider 声明的 <c>SessionStorePaths</c>），所以执行目标对删除时的询问（<see cref="CliLaunchProfilePurpose.SessionCleanup"/>）
    /// 要给出与启动时相同的目录与项目目录名。
    /// </remarks>
    public string? ConfigDirectory { get; init; }

    /// <summary>
    /// <see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/> 时会话记录所在的项目目录名（claude：<c>CLAUDE_CODE_PROJECT_DIR_NAME</c>）。
    /// null = CLI 按工作目录的路径命名。
    /// </summary>
    /// <remarks>
    /// 实测 claude 2.1：设了它，同一项目的两个 worktree 的记录都落在 <c>&lt;配置目录&gt;/projects/&lt;名字&gt;/&lt;会话 id&gt;.jsonl</c>，
    /// 在另一个 worktree 里 <c>--resume</c> 能续上、会话 id 不变；不设时按路径命名，换一个 worktree 就续接不上。
    /// </remarks>
    public string? ProjectDirectoryName { get; init; }

    /// <summary>
    /// 查不到登录态时能否注入部署的兜底令牌（<c>FallbackAuthToken</c>）。缺省 true（与此前相同）。
    /// </summary>
    /// <remarks>
    /// 置 false 表示这一轮的凭据只能来自执行位置自己的登录或 <see cref="Environment"/>（例如用户自己的订阅或 key）。
    /// 远程运行时上的运行，框架在 contributor 之后再按 provider 的 <c>AllowFallbackAuthTokenOnRemoteTargets</c> 把它收成 false：
    /// 服务器的凭据不进用户的工作区，contributor 不能把它打开。
    /// </remarks>
    public bool UseFallbackAuthToken { get; init; } = true;
}

/// <summary>为哪件事询问启动档案。</summary>
public enum CliLaunchProfilePurpose
{
    /// <summary>起一个 CLI 进程（每次执行一次，续接被拒等重试沿用同一份）。</summary>
    Launch = 0,

    /// <summary>
    /// 线程被删除，要找到它的会话记录：只读 <see cref="CliLaunchProfile.ConfigDirectory"/> 与 <see cref="CliLaunchProfile.ProjectDirectoryName"/>。
    /// </summary>
    SessionCleanup = 1
}

/// <summary>
/// 询问一次运行的启动档案时交给执行目标与 <see cref="ICliLaunchProfileContributor"/> 的信息。
/// </summary>
/// <remarks>身份一律取自运行记录（见 <see cref="CliHostScope"/>）；<see cref="CliHostScope.RunId"/> 可以拿去查这一轮的其它事实。</remarks>
[ExperimentalApi(Reason = "Per-run launch settings are new; the approval channel for CliPermissionMode.Ask will build on it")]
public sealed record CliLaunchProfileRequest
{
    /// <summary>这次运行的身份（取执行目标时用的那一份）。</summary>
    public required CliHostScope Scope { get; init; }

    /// <summary>provider 描述。</summary>
    public required CliProviderDescriptor Provider { get; init; }

    /// <summary>工作目录（执行目标上的路径）。</summary>
    public required string WorkDirectory { get; init; }

    /// <summary>工作目录是否是用户提供的（绑定的 <c>UserProvided</c> 模式）。</summary>
    public bool WorkDirectoryIsUserOwned { get; init; }

    /// <summary>为哪件事询问。</summary>
    public CliLaunchProfilePurpose Purpose { get; init; } = CliLaunchProfilePurpose.Launch;
}

/// <summary>
/// 按这一次运行调整启动档案（权限档位、内置工具、MCP 合并、设置……）。
/// </summary>
/// <remarks>
/// <para>
/// 在执行目标给出档案之后依次调用，后一个拿到前一个的结果；没有注册时档案就是执行目标给的那一份。
/// 适合「调用方」的决定：例如用户为这一轮选的权限档位，存在消费方自己那边、按 <see cref="CliHostScope.RunId"/> 或线程取回。
/// 在消费方模块的 <c>ConfigureServicesAsync</c> 里注册为 Scoped：
/// </para>
/// <code>
/// context.Services.AddScoped&lt;ICliLaunchProfileContributor, ChatModeLaunchProfile&gt;();
/// </code>
/// <para>抛出的异常让这一轮以 <c>LaunchFailed</c> 结束（<see cref="CliProviderConfigurationException"/> 的消息原样写进运行记录）。</para>
/// </remarks>
[ExperimentalApi(Reason = "Per-run launch settings are new; the approval channel for CliPermissionMode.Ask will build on it")]
public interface ICliLaunchProfileContributor
{
    /// <summary>返回调整后的档案（不改就原样返回 <paramref name="profile"/>）。</summary>
    Task<CliLaunchProfile> ContributeAsync(CliLaunchProfileRequest request, CliLaunchProfile profile, CancellationToken cancellationToken);
}

/// <summary>按「执行目标 → 各个 contributor」的次序得出一次运行的启动档案。执行器与线程删除的清理共用它。</summary>
internal static class CliLaunchProfileResolver
{
    public static async Task<CliLaunchProfile> ResolveAsync(
        ICliHostTarget target, IEnumerable<ICliLaunchProfileContributor> contributors, CliLaunchProfileRequest request,
        CancellationToken cancellationToken)
    {
        Check.NotNull(target);
        Check.NotNull(contributors);
        Check.NotNull(request);

        var profile = await target.GetLaunchProfileAsync(request, cancellationToken) ?? CliLaunchProfile.Default;
        foreach (var contributor in contributors)
        {
            profile = await contributor.ContributeAsync(request, profile, cancellationToken) ?? profile;
        }

        return profile;
    }
}
