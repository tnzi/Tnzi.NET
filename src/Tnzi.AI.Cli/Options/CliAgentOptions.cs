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
    /// 空 = <c>{WorkspacesRoot}/.cli-config/{provider}</c>。
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
    /// </remarks>
    public string? FallbackAuthToken { get; set; }
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
    /// 框架 MCP server 的<b>绝对</b> SSE 端点 URL（如 <c>https://api.example.com/mcp</c>）。
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
    /// 持运行范围凭据的 agent 在框架 MCP server 上<b>允许调用的工具名</b>（大小写不敏感）。
    /// <c>["*"]</c> 表示全部；启用回写时必须显式给出（验证器拒绝空列表）—— 回写面是一个安全决定，不能靠缺省。
    /// </summary>
    /// <remarks>
    /// ★ 此前凭据校验完就被丢掉，持凭据的子进程与一把静态 API Key 拿到的是同一个面
    /// （全部暴露的 agent + 全部消费方自定义工具），而文档写着「上限是该 Agent 自身的权限」。
    /// 该子进程以 <c>--permission-mode bypassPermissions</c> 跑任意代码，一次提示注入就是整个面。
    /// </remarks>
    public List<string> AllowedTools { get; set; } = [];
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
    /// 可再生目录名。<b>只匹配 basename</b>：含路径分隔符的条目会被静默丢弃，
    /// 否则一条 <c>"../.."</c> 就能把回收器变成删库工具。
    /// </summary>
    public List<string> ArtifactPatterns { get; set; } =
        ["node_modules", ".next", ".turbo", "bin", "obj", "target", "__pycache__", ".venv"];
}
