namespace Tnzi.AI.Cli.Options;

/// <summary>
/// 外部 CLI agent 执行的部署级配置（<c>AI:Cli</c>）。
/// </summary>
[ConfigSection("AI:Cli")]
public class CliAgentOptions
{
    /// <summary>
    /// 总开关。<b>默认关闭</b> —— 外部 agent 等于任意代码执行能力，必须显式开启。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 隔离工作区根目录。null = 回落到 <c>{LocalApplicationData}/Tnzi/agent-workspaces</c>。
    /// </summary>
    public string? WorkspacesRoot { get; set; }

    /// <summary>
    /// 是否按派出运行的用户再分一层目录：<c>{root}/{tenant}/u-{userId}/{thread|run}</c>，
    /// 没有登录用户的运行落在 <c>u-anonymous</c>。<b>默认 false</b>（<c>{root}/{tenant}/{thread|run}</c>）。
    /// </summary>
    /// <remarks>
    /// 编码 CLI 按工作目录存会话存档，所以切换这个开关会让既有线程换一个目录 ——
    /// 那些线程的下一轮续接必被拒，框架按「续接被拒」处理：开新会话并告知上下文已丢失。
    /// </remarks>
    public bool PartitionWorkspacesByUser { get; set; }

    /// <summary>本进程最大并发外部运行数。</summary>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>队列轮询间隔。</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>认领租约时长。宿主崩溃后到期即被回收。</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>运行时探测间隔。</summary>
    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 空闲看门狗：完全没有任何事件超过此时长即判定卡死。
    /// </summary>
    public TimeSpan IdleWatchdog { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>工具看门狗：有 tool-use 却迟迟收不到 tool-result 的上限。</summary>
    public TimeSpan ToolWatchdog { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>握手超时：ACP initialize / 会话建立阶段的上限。</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 硬超时（挂钟总时长）。<b>默认 null = 关闭</b>：一个持续产出事件的长任务，
    /// 不该仅仅因为跑得久就被杀掉。它只作为最后保险，由部署自行决定是否需要。
    /// </summary>
    public TimeSpan? HardTimeout { get; set; }

    /// <summary>整树终止的宽限期：先温和请求退出，超时才强杀。</summary>
    public TimeSpan TerminateGrace { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 是否把宿主的全部环境变量透传给子进程。<b>默认 false</b>。
    /// </summary>
    /// <remarks>
    /// 打开它等于把应用的数据库连接串、签名密钥、云凭据一并交给一个能执行任意命令的子进程。
    /// 默认只透传安全基线（PATH / HOME / TEMP / 语言区域等）+ <see cref="EnvironmentWhitelist"/>。
    /// </remarks>
    public bool InheritAllHostEnvironment { get; set; }

    /// <summary>额外透传的环境变量名白名单（如 provider 的 API key 变量）。</summary>
    public List<string> EnvironmentWhitelist { get; set; } = [];

    /// <summary>工作区回收配置。</summary>
    public CliWorkspaceGcOptions Gc { get; set; } = new();

    /// <summary>回写通道配置。</summary>
    public CliWriteBackOptions WriteBack { get; set; } = new();

    /// <summary>工具结果里图片的上限。</summary>
    public CliToolResultOptions ToolResults { get; set; } = new();

    /// <summary>服务端重启后重新接上仍在远程宿主上运行的进程（只对交出重新附着句柄的宿主生效）。</summary>
    public CliReattachOptions Reattach { get; set; } = new();

    /// <summary>远程执行目标不在线时请使用方唤起、等它上线（只对 <c>CliRuntimeMode.RemoteDaemon</c> 的运行时生效）。</summary>
    public CliActivationOptions Activation { get; set; } = new();

    /// <summary>「先问我」档位下权限请求的批准（<c>AI:Cli:Approval</c>）。</summary>
    public CliApprovalOptions Approval { get; set; } = new();

    /// <summary>
    /// 本机宿主上，使用方按运行指定的工作目录（<c>CliRunPlacement.WorkDirectory</c>）允许是哪些根目录本身或落在其下（绝对路径）。默认空 = 本机不接受按运行指定的工作目录。
    /// </summary>
    /// <remarks>
    /// 不能与 <see cref="WorkspacesRoot"/> 重叠：受管工作区的回收器会把那里的子目录当成运行目录删掉。远程执行目标的根由目标自己声明
    /// （<c>ICliHostTarget.WorkDirectoryRoots</c>），不读这一项。
    /// </remarks>
    public List<string> AssignedWorkDirectoryRoots { get; set; } = [];

    /// <summary>按 provider 键的部署级覆盖。</summary>
    public Dictionary<string, CliProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 自定义 provider：让部署接入一个内置表里没有、但说已支持协议的 CLI，<b>不改代码</b>。
    /// </summary>
    public List<CliCustomProviderOptions> CustomProviders { get; set; } = [];

    /// <summary>
    /// 某个 provider 的部署级配置：<see cref="CustomProviders"/> 优先（它能整体替换内置项），其次 <see cref="Providers"/>。
    /// </summary>
    public CliProviderOptions? FindProviderOptions(string providerKey)
        => CustomProviders.LastOrDefault(c => string.Equals(c.Key, providerKey, StringComparison.OrdinalIgnoreCase))
           ?? Providers.GetValueOrDefault(providerKey);
}

/// <summary>
/// 单个 provider 的部署级覆盖。
/// </summary>
public class CliProviderOptions
{
    /// <summary>是否启用本 provider。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>可执行文件绝对路径。空 = 走 PATH 查找默认名。</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>部署级默认模型。</summary>
    public string? DefaultModel { get; set; }

    /// <summary>部署级默认追加参数，先于每 agent 的自定义参数。</summary>
    public List<string> ExtraArgs { get; set; } = [];

    /// <summary>
    /// 与宿主账号个人配置的隔离档位。<b>默认 <see cref="CliUserConfigIsolation.ExcludeUserSettings"/></b>：
    /// 个人 hooks、插件、技能不进受管运行，登录态照用。
    /// </summary>
    public CliUserConfigIsolation UserConfigIsolation { get; set; } = CliUserConfigIsolation.ExcludeUserSettings;

    /// <summary>
    /// <see cref="CliUserConfigIsolation.IsolatedConfigDirectory"/> 时的专用配置目录（绝对路径）。
    /// 空 = <c>{WorkspacesRoot}/.cli-config/{provider}</c>。别的档位不读它
    /// （<see cref="CliUserConfigIsolation.PerWorkspaceConfigDirectory"/> 的目录由工作区决定，
    /// <see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/> 的由运行的启动档案给出），配了会在启动日志里告警。
    /// </summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    /// 兜底认证令牌（claude：<c>claude setup-token</c> 生成的长期令牌，走订阅额度）。
    /// </summary>
    /// <remarks>
    /// <b>只在查不到本机登录态时注入</b>：每次启动前先以运行将要使用的同一份环境查一次登录状态，
    /// 已登录（含白名单透传的 API key）就不注入 —— 令牌优先级高于本机登录，无条件注入会顶掉它。
    /// 查询不可用或失败时视为未登录，照样注入。
    /// <para>这是机密：放 user-secrets / 环境变量（<c>AI__Cli__Providers__claude__FallbackAuthToken</c>），不要写进 appsettings.json。</para>
    /// <para>只交给本机运行时上的运行；远程执行目标上的运行要另外打开 <see cref="AllowFallbackAuthTokenOnRemoteTargets"/>。</para>
    /// </remarks>
    public string? FallbackAuthToken { get; set; }

    /// <summary>
    /// <see cref="FallbackAuthToken"/> 是否也交给远程执行目标（<c>CliRuntimeMode.RemoteDaemon</c>）上的运行。默认 false：
    /// 那里的进程能读到自己的环境变量，令牌送过去就是交给了在那个工作区里跑的人。远程运行的凭据由启动档案的 <c>Environment</c> 或配置目录给出。
    /// </summary>
    public bool AllowFallbackAuthTokenOnRemoteTargets { get; set; }

    /// <summary>
    /// 接受的最低 CLI 版本（含），如 <c>2.1.300</c>。空 = 不限。运行时登记的版本低于它或认不出版本时，这家 CLI 上的运行不跑。
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// 接受的最高 CLI 版本（含），只比较写出来的那几段：<c>2.1</c> = 任何 2.1.x。空 = 不限。
    /// </summary>
    public string? MaximumVersion { get; set; }
}

/// <summary>
/// 一个由配置声明的自定义 provider。
/// </summary>
public class CliCustomProviderOptions : CliProviderOptions
{
    /// <summary>provider 键。与内置键冲突时覆盖内置项。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>展示名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>协议族。必须是本版本已实现适配器的协议。</summary>
    public CliAgentProtocol Protocol { get; set; } = CliAgentProtocol.Acp;

    /// <summary>默认可执行文件名。</summary>
    public string DefaultExecutable { get; set; } = string.Empty;

    /// <summary>启动时追加的固定子命令/参数。</summary>
    public List<string> LaunchArgs { get; set; } = [];

    /// <summary>原生读取的记忆文件名。空 = 内联进 system prompt。</summary>
    public string? BriefFileName { get; set; }

    /// <summary>原生发现 skill 的相对目录。空 = 不物化 skill。</summary>
    public string? SkillsRelativePath { get; set; }

    /// <summary>启动骨架预览。</summary>
    public string? LaunchHeader { get; set; }

    /// <summary>查询登录状态的参数（输出含 <c>loggedIn</c> 的 JSON）。空 = 无从查询。</summary>
    public List<string> AuthStatusArgs { get; set; } = [];

    /// <summary>兜底令牌经由的环境变量名。空 = 不支持 <see cref="CliProviderOptions.FallbackAuthToken"/>。</summary>
    public string? AuthTokenEnvironmentVariable { get; set; }

    /// <summary><see cref="CliUserConfigIsolation.ExcludeUserSettings"/> 对应的启动参数。空 = 该档不生效。</summary>
    public List<string> ExcludeUserSettingsArgs { get; set; } = [];

    /// <summary>重定向配置目录的环境变量名。空 = 不支持 <see cref="CliUserConfigIsolation.IsolatedConfigDirectory"/>。</summary>
    public string? ConfigDirectoryEnvironmentVariable { get; set; }

    /// <summary>权限档位不是全权时代替 <see cref="LaunchArgs"/> 的启动参数（去掉了「一律自动批准」开关的那一列）。空 = 与 <see cref="LaunchArgs"/> 相同。</summary>
    public List<string> LaunchArgsWithoutAutoApproval { get; set; } = [];

    /// <summary>给出项目目录名的环境变量名。空 = 不支持。</summary>
    public string? ProjectDirectoryNameEnvironmentVariable { get; set; }

    /// <summary>会话记录相对配置目录的路径模板（<c>{project}</c>、<c>{session}</c>）。空 = 删除线程时不删记录。</summary>
    public List<string> SessionStorePaths { get; set; } = [];

    /// <summary>以文件给出追加系统提示的参数。空 = 不支持。</summary>
    public string? AppendSystemPromptFileArg { get; set; }

    /// <summary>附加目录的参数。空 = 不支持。</summary>
    public string? AddDirectoryArg { get; set; }

    /// <summary>限定内置工具的参数（取值逗号分隔）。空 = 不支持。</summary>
    public string? BuiltInToolsArg { get; set; }

    /// <summary>选择设置来源的参数（取值逗号分隔）。空 = 不支持。</summary>
    public string? SettingSourcesArg { get; set; }

    /// <summary>以 JSON 给出附加设置的参数。空 = 不支持。</summary>
    public string? SettingsArg { get; set; }

    /// <summary>附加设置里表示会话记录保留天数的键。空 = 不支持。</summary>
    public string? SessionRetentionSettingName { get; set; }
}

/// <summary>
/// 回写通道配置：让外部 agent 经框架自己的 MCP server 反向调用平台。
/// </summary>
/// <remarks>
/// <b>默认关闭</b>：打开它等于给一个能执行任意代码的子进程一把能调平台的钥匙。
/// 即使打开，钥匙也是运行范围的：随运行结束失效，且只开得了 <see cref="AllowedTools"/> 点名的那几扇门
/// （MCP server 按凭据自带的调用面过滤 <c>tools/list</c>、拒绝越界的 <c>tools/call</c>）。
/// </remarks>
public class CliWriteBackOptions
{
    /// <summary>是否为每次运行注入回写通道。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 框架 MCP server（<c>AI:McpServer:Endpoint</c>）的<b>绝对</b> URL（如 <c>https://api.example.com/mcp</c>），
    /// 以 Streamable HTTP（MCP 配置里的 <c>"type": "http"</c>）连接。
    /// </summary>
    /// <remarks>
    /// 必须由部署方给出：框架无法可靠推断自己对外的公开地址（反代、多域名、内外网分离
    /// 都会让进程内看到的地址与 agent 实际能访问的地址不同）。
    /// </remarks>
    public string? McpEndpoint { get; set; }

    /// <summary>注入到 agent MCP 配置里的服务器名。</summary>
    public string ServerName { get; set; } = "tnzi";

    /// <summary>凭据有效期。运行提前结束时也一并失效，两者取先到的那个。</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// 一次回写工具调用最多等多久。<b>默认 null = 不写</b>，CLI 用它自己的缺省。
    /// </summary>
    /// <remarks>
    /// 以毫秒写进回写条目的 <c>timeout</c> 字段。claude 把它当作这个 server 每次调用的上限（进度通知不延长它），
    /// 同时也是它的 HTTP 传输<b>等响应开始</b>的时长，没有这个字段时是 60 秒；低于 60 秒的值被抬到 60 秒。
    /// 别的 stream-json CLI 是否读这个字段未经验证。
    /// <para>
    /// 回写端点前面有一层把响应攒到完成才放行的代理或网关时，这 60 秒就是每一次回写调用的上限：
    /// 跑得更久的调用被 CLI 判为 "The operation timed out."，而消费方工具其实还在干活。
    /// 消费方工具跑几分钟是常态（远程命令、安装、拷贝目录）时就要配它；直连、响应头立刻发出的部署不受这 60 秒影响。
    /// </para>
    /// <para>
    /// 只作用于回写条目，绑定自带的 MCP server 用它们自己 JSON 里的值。ACP 的 <c>mcpServers</c> 没有这个字段，ACP 运行时收不到它。
    /// 必须短于 <see cref="CliAgentOptions.ToolWatchdog"/>（验证器守着）：工具看门狗先到点时判的是整次运行超时，
    /// 而不是这一次调用超时、agent 拿着超时结果接着干。
    /// </para>
    /// </remarks>
    public TimeSpan? ToolCallTimeout { get; set; }

    /// <summary>
    /// 持运行范围凭据的 agent 在框架 MCP server 上<b>允许调用的工具名</b>（大小写不敏感）。
    /// <c>["*"]</c> 表示全部；启用回写时必须显式给出（验证器拒绝空列表）—— 回写面是一个安全决定，不能靠缺省。
    /// 再加 <c>"@user-mcp"</c>（<c>RunScopedCredential.UserMcpTools</c>）才会转发派发者自己连接的第三方 MCP 服务器的工具，
    /// <c>*</c> 不涵盖它。
    /// </summary>
    /// <remarks>
    /// ★ 此前凭据校验完就被丢掉，持凭据的子进程与一把静态 API Key 拿到的是同一个面
    /// （全部暴露的 agent + 全部消费方自定义工具），而文档写着「上限是该 Agent 自身的权限」。
    /// 该子进程以 <c>--permission-mode bypassPermissions</c> 跑任意代码，一次提示注入就是整个面。
    /// </remarks>
    public List<string> AllowedTools { get; set; } = [];
}

/// <summary>
/// 工具结果里图片的上限（截图这类：工具把图片作为结果的一部分返回）。
/// </summary>
/// <remarks>
/// 图片存成附件（随回复保存，线程重新加载时照样显示），超出上限的不存，原处换成一行说明哪张图因何没存。
/// 工具结果的文本照旧按每次调用 32,768 个字符截断（末尾注明截掉了多少）。
/// </remarks>
public class CliToolResultOptions
{
    /// <summary>单张图片的最大字节数（解码后）。默认 5 MB（与 Claude API 对单张图片的上限一致）。</summary>
    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>一次工具调用的结果最多存几张图片。默认 8。</summary>
    public int MaxImagesPerResult { get; set; } = 8;
}

/// <summary>
/// 重新附着：服务端停机时留在执行宿主上继续跑的进程，重启后怎么接回来。
/// </summary>
/// <remarks>
/// 只对起进程时交出了重新附着句柄的宿主生效（<c>ICliProcess.ReattachHandle</c>）。本机宿主不交句柄：
/// 它的进程随 API 进程一起结束，停机时的运行照旧打回队列重跑。
/// </remarks>
public class CliReattachOptions
{
    /// <summary>
    /// 从没有服务端在驱动这条运行算起，最多尝试多久。过了还接不上（宿主一直不可用），这一轮以 <c>Interrupted</c> 结束。
    /// 默认 10 分钟。执行宿主保留一个没人接的进程至少要这么久（再加一个 <see cref="CliAgentOptions.LeaseDuration"/>，崩溃时要先等租约过期）。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>宿主暂时不可用时，隔多久再试一次。默认 15 秒。</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 重放时等执行器重新发出某一行之前那些 stdin 写入的上限。默认 10 秒。
    /// </summary>
    /// <remarks>
    /// 重放按原来的先后次序把 stdout 行交给适配器：一行要等到适配器重新发出了进程写它之前收到的那些 stdin 行才交出。
    /// 只有适配器这一次的行为与上一次不一样（绑定在中途改了模型）时才会等满，那时照常交出这一行并记一条 Warning。
    /// </remarks>
    public TimeSpan ReplayOrderingWait { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// 「先问我」档位（<c>CliPermissionMode.Ask</c>）在有人值守的一轮上把权限请求交给使用方批准。
/// </summary>
public class CliApprovalOptions
{
    /// <summary>
    /// 一个权限请求等回答的上限，从请求出现时算起（服务端重启不重新计时）。过了没人回答就按拒绝交给 CLI。默认 10 分钟：
    /// 手机上的使用者往往隔几分钟才看到。等待期间空闲与工具看门狗暂停，硬超时照常计时。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 执行这次运行的服务端副本多久查一次库里的回答。默认 2 秒。回答落在同一个副本上时立刻生效，不等这一轮。
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// 远程执行目标的上线与唤起：运行排在一个此刻没有服务端副本连着的执行目标上时，怎样请使用方把它起来、等多久。
/// </summary>
/// <remarks>
/// 「在线」= 某个服务端副本的执行宿主此刻服务这个目标（<c>ICliHost.GetCoverage</c>）：这样的副本每轮把它的
/// <c>LastSeenAt</c> 刷到现在。<see cref="OnlineTtl"/> 内没人刷过就算不在线。
/// </remarks>
public class CliActivationOptions
{
    /// <summary>从第一次请求唤起算起，等执行目标上线的上限。过了还没上线，排在它上面的运行以 <c>TargetUnavailable</c> 结束。默认 2 分钟。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>还没上线时，隔多久再请使用方唤起一次（多个副本之间也按它去重）。默认 15 秒。</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>远程运行时最近一次被服务（<c>LastSeenAt</c>）距今不超过它才算在线。默认 30 秒，须大于两个 <see cref="CliAgentOptions.PollInterval"/>。</summary>
    public TimeSpan OnlineTtl { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 工作区回收配置。
/// </summary>
public class CliWorkspaceGcOptions
{
    /// <summary>是否启用回收。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>回收扫描间隔。</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(2);

    /// <summary>运行终态后多久删掉整个运行目录。</summary>
    public TimeSpan CompletedTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>无回收元数据的孤儿目录存活上限。</summary>
    public TimeSpan OrphanTtl { get; set; } = TimeSpan.FromHours(72);

    /// <summary>会话仍活跃但产物可再生时，多久清一次可再生目录。</summary>
    public TimeSpan ArtifactTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// 可再生目录名，留空表示用 <see cref="DefaultArtifactPatterns"/>；配了就<b>整体替换</b>默认列表。
    /// <b>只匹配 basename</b>：含路径分隔符的条目会被静默丢弃，否则一条 <c>"../.."</c> 就能把回收器变成删库工具。
    /// </summary>
    /// <remarks>
    /// ★ 默认值必须是空列表：.NET 的配置绑定对预置了元素的 <c>List</c> 是<b>追加</b>语义。此前这里预置了默认名单，
    /// 部署方配 <c>["node_modules"]</c> 想收窄，生效的是默认名单再加一条，除了关掉整个回收没有办法让它少删一个名字。
    /// 缺省值改在读取处补（见 <see cref="ResolveArtifactPatterns"/>）。
    /// </remarks>
    public List<string> ArtifactPatterns { get; set; } = [];

    /// <summary>
    /// 没配 <see cref="ArtifactPatterns"/> 时用的可再生目录名。
    /// </summary>
    /// <remarks>
    /// 刻意不含 <c>bin</c>：它在很多生态里是受版本控制的源码目录（Rails 的 binstubs、shell 工具仓库的脚本），
    /// agent 在每线程工作区里克隆了这样的仓库，闲置超过 <see cref="ArtifactTtl"/> 后 <c>bin/</c> 连同未提交的修改一起被删。
    /// 需要回收 .NET 构建输出的部署自己把它配回去（配置是整体替换，要连同想保留的默认项一起写）。
    /// </remarks>
    public static IReadOnlyList<string> DefaultArtifactPatterns { get; } =
        ["node_modules", ".next", ".turbo", "obj", "target", "__pycache__", ".venv"];

    /// <summary>
    /// 取生效的可再生目录名：配了就用配的，没配用 <see cref="DefaultArtifactPatterns"/>。
    /// </summary>
    public IReadOnlyList<string> ResolveArtifactPatterns()
        => ArtifactPatterns is { Count: > 0 } configured ? configured : DefaultArtifactPatterns;
}
