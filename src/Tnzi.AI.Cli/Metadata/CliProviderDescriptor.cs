namespace Tnzi.AI.Cli.Metadata;

/// <summary>
/// 协议族。决定用哪个适配器驱动会话。
/// </summary>
public enum CliAgentProtocol
{
    /// <summary>stdio 上的行分隔 JSON 事件流（claude / codebuddy / qwen / cursor）。</summary>
    StreamJson = 0,

    /// <summary>Agent Client Protocol：stdio 上的 JSON-RPC 2.0，双向（hermes / kimi / kiro / qoder / trae / grok）。</summary>
    Acp = 1,

    /// <summary>厂商专有 app-server（codex）。</summary>
    VendorAppServer = 2
}

/// <summary>
/// 被禁参数的匹配模式。
/// </summary>
public enum BlockedArgMode
{
    /// <summary>带值参数，如 <c>--settings &lt;path&gt;</c>：命中后需连带吞掉下一个 token。</summary>
    WithValue = 0,

    /// <summary>独立开关，如 <c>acp</c> / <c>-p</c>。</summary>
    Standalone = 1
}

/// <summary>
/// 单个外部 agent provider 的静态描述。内置集合 + appsettings 扩展合并而成。
/// </summary>
/// <remarks>
/// <para>
/// 这是本模块相对同类实现的<b>主要结构改进</b>：把「协议」和「provider 参数」分开 ——
/// 协议是代码（写适配器），provider 参数是数据（写描述表）。参考实现里 18 个 provider
/// 各占一个源文件、合计近 2MB，其中大量内容只是可执行文件名、记忆文件名、skills 目录、
/// 被禁参数这些**数据**的重复。
/// </para>
/// <para>
/// 直接结果：加一个说 ACP 的新 CLI <b>不需要改代码</b>，往 <c>AI:Cli:CustomProviders</c>
/// 里加一条即可。
/// </para>
/// </remarks>
public sealed record CliProviderDescriptor
{
    /// <summary>provider 键，如 "claude"。与配置节 key 一致。</summary>
    public required string Key { get; init; }

    /// <summary>界面展示名，如 "Claude Code"。</summary>
    public required string DisplayName { get; init; }

    /// <summary>协议族，决定用哪个适配器。</summary>
    public required CliAgentProtocol Protocol { get; init; }

    /// <summary>默认可执行文件名（PATH 查找），如 "claude"。</summary>
    public required string DefaultExecutable { get; init; }

    /// <summary>启动时追加的固定子命令/参数，如 ACP 的 <c>["acp"]</c>。</summary>
    public IReadOnlyList<string> LaunchArgs { get; init; } = [];

    /// <summary>
    /// 权限档位不是 <see cref="CliPermissionMode.Full"/> 时代替 <see cref="LaunchArgs"/> 的启动参数。null = 与 <see cref="LaunchArgs"/> 相同。
    /// </summary>
    /// <remarks>
    /// 给那些在 <see cref="LaunchArgs"/> 里带着「一律自动批准」开关的 ACP CLI 用（qoder 的 <c>--yolo</c>、grok 的 <c>--always-approve</c>）：
    /// 带着它，CLI 从不发 <c>session/request_permission</c>，档位无从落实。整列替换而不是删掉一个开关，是因为开关在子命令之间的位置有讲究。
    /// </remarks>
    public IReadOnlyList<string>? LaunchArgsWithoutAutoApproval { get; init; }

    /// <summary>
    /// 该 provider 原生读取的记忆文件名，如 <c>CLAUDE.md</c> / <c>AGENTS.md</c> / <c>QWEN.md</c>。
    /// 空 = 不写文件，改为内联进 system prompt。
    /// </summary>
    public string? BriefFileName { get; init; }

    /// <summary>
    /// 该 provider 原生发现 skill 的相对目录，如 <c>.claude/skills</c>。空 = 不物化 skill。
    /// </summary>
    public string? SkillsRelativePath { get; init; }

    /// <summary>用户可见的启动骨架预览，如 "claude (stream-json)"。</summary>
    public string? LaunchHeader { get; init; }

    /// <summary>
    /// 该 provider 能否区分「resume 被拒绝」与其它启动失败。
    /// </summary>
    /// <remarks>
    /// 默认 <c>false</c> = <b>fail-closed</b>：分不清就不做 fresh-session 重试，绝不猜。
    /// 猜错的代价是丢掉一整段可恢复的会话上下文并重跑一次任务 —— 比多报一次失败严重得多。
    /// <para>
    /// 读者是 stream-json 适配器：它判「被拒」靠的是对内置 provider 实测出来的 stderr 短语与
    /// 「非零退出 + 0 轮 + 0 成本」的佐证，只对声明了这一位的 provider 使用。ACP 适配器不读它：
    /// 那里只在 JSON-RPC 错误明说会话不存在时置位，本身就是正面证据。
    /// </para>
    /// </remarks>
    public bool ResumeRejectionDetectable { get; init; }

    /// <summary>
    /// 该 provider 能否随提示接收附件（PNG / JPEG / GIF / WebP 图片与 PDF 文档，作为内容块投递）。
    /// </summary>
    /// <remarks>
    /// 默认 <c>false</c>：带附件的消息发给不支持的 provider 时当场以 415 拒绝，而不是只把文字发出去：
    /// 那样的回合看上去和成功一模一样，模型照样作答，用户以为它看到了附件。只对实测过的 provider 打开。
    /// </remarks>
    public bool AcceptsAttachments { get; init; }

    /// <summary>
    /// 该 provider 能否从会话里的某个条目处截断续接（claude：<c>--resume-session-at &lt;条目&gt; --fork-session</c>）。
    /// </summary>
    /// <remarks>
    /// 编辑与重新生成要丢掉会话里最近的几轮。能截断的 provider 从被保留的最后一轮处分叉出新会话，
    /// 被替换的轮次就不在 agent 的记忆里了；不能截断的开一个新会话，并在这一轮的上下文里说明之前的对话已不可用。
    /// 默认 <c>false</c>：只对实测过的 provider 打开。
    /// </remarks>
    public bool SupportsResumeAtEntry { get; init; }

    /// <summary>禁止用户 custom args 覆盖的参数（协议契约参数）。</summary>
    public IReadOnlyDictionary<string, BlockedArgMode> BlockedArgs { get; init; }
        = new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal);

    /// <summary>该 provider 是否必须内联投递 system prompt（无原生记忆文件时）。</summary>
    public bool RequiresInlineSystemPrompt { get; init; }

    /// <summary>
    /// 查询登录状态的参数，输出须为含 <c>loggedIn</c> 布尔字段的 JSON（claude：<c>auth status --json</c>）。
    /// 空 = 无从查询，配置了兜底令牌时一律注入。
    /// </summary>
    public IReadOnlyList<string> AuthStatusArgs { get; init; } = [];

    /// <summary>
    /// 兜底令牌经由的环境变量名（claude：<c>CLAUDE_CODE_OAUTH_TOKEN</c>，由 <c>claude setup-token</c> 生成）。
    /// 空 = 该 provider 不支持令牌兜底。
    /// </summary>
    public string? AuthTokenEnvironmentVariable { get; init; }

    /// <summary>
    /// <see cref="CliUserConfigIsolation.ExcludeUserSettings"/> 对应的启动参数
    /// （claude：<c>--setting-sources project,local</c>）。空 = 该档对本 provider 不生效。
    /// </summary>
    public IReadOnlyList<string> ExcludeUserSettingsArgs { get; init; } = [];

    /// <summary>
    /// 重定向整个配置目录的环境变量名（claude：<c>CLAUDE_CONFIG_DIR</c>）。
    /// 空 = 不支持 <see cref="CliUserConfigIsolation.IsolatedConfigDirectory"/> 及之后的几档。
    /// </summary>
    public string? ConfigDirectoryEnvironmentVariable { get; init; }

    /// <summary>
    /// <see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/> 时给出项目目录名的环境变量（claude：<c>CLAUDE_CODE_PROJECT_DIR_NAME</c>）。
    /// 空 = 不支持按名字固定项目目录。
    /// </summary>
    public string? ProjectDirectoryNameEnvironmentVariable { get; init; }

    /// <summary>
    /// 一个会话在配置目录里留下的记录，相对配置目录的路径模板（<c>{project}</c> = 项目目录名，<c>{session}</c> = 会话 id）。
    /// 删除线程时按会话 id 删掉它们；文件与目录都可以，不存在的跳过。空 = 不知道记录在哪里，不删。
    /// </summary>
    public IReadOnlyList<string> SessionStorePaths { get; init; } = [];

    /// <summary>以文件给出追加系统提示的参数（claude：<c>--append-system-prompt-file</c>）。空 = 不支持。</summary>
    /// <remarks>
    /// 工作目录是用户提供的时，框架不往里面写记忆文件，agent 说明经它投递；不支持的 provider 把说明内联进新会话的第一条提示。
    /// </remarks>
    public string? AppendSystemPromptFileArg { get; init; }

    /// <summary>附加目录的参数（claude：<c>--add-dir</c>，每个目录一次）。空 = 不支持。</summary>
    /// <remarks>
    /// 工作目录是用户提供的时，技能物化在运行目录里、经它交给 CLI；不支持的 provider 在这种工作目录里不物化技能。
    /// </remarks>
    public string? AddDirectoryArg { get; init; }

    /// <summary>限定内置工具的参数（claude：<c>--tools</c>，取值逗号分隔，空串 = 不开任何内置工具）。空 = 不支持。</summary>
    public string? BuiltInToolsArg { get; init; }

    /// <summary>选择设置来源的参数（claude：<c>--setting-sources</c>，取值逗号分隔）。空 = 不支持。</summary>
    public string? SettingSourcesArg { get; init; }

    /// <summary>以 JSON 给出附加设置的参数（claude：<c>--settings</c>）。空 = 不支持。</summary>
    public string? SettingsArg { get; init; }

    /// <summary>附加设置里表示会话记录保留天数的键（claude：<c>cleanupPeriodDays</c>）。空 = 不支持。</summary>
    public string? SessionRetentionSettingName { get; init; }

    /// <summary>部署级的个人配置隔离档位。</summary>
    public CliUserConfigIsolation UserConfigIsolation { get; init; } = CliUserConfigIsolation.ExcludeUserSettings;

    /// <summary>
    /// <see cref="CliUserConfigIsolation.IsolatedConfigDirectory"/> 时的专用配置目录（绝对路径）。
    /// 空 = <c>{WorkspacesRoot}/.cli-config/{Key}</c>。
    /// </summary>
    public string? ConfigDirectory { get; init; }

    /// <summary>本 provider 是否在当前部署启用（由 Options 合并时填充）。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>配置指定的可执行文件绝对路径。空 = 走 PATH 查找 <see cref="DefaultExecutable"/>。</summary>
    public string? ExecutablePathOverride { get; init; }

    /// <summary>部署级默认模型。</summary>
    public string? DefaultModel { get; init; }

    /// <summary>部署级默认追加参数，先于每 agent 的自定义参数。</summary>
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];

    /// <summary>
    /// 这个部署接受的最低 CLI 版本（含），如 <c>2.1.300</c>。空 = 不限。比较按数字逐段，缺的段按 0。
    /// </summary>
    /// <remarks>运行时登记的版本低于它、或者认不出版本时，这家 CLI 上的运行不跑（见 <see cref="CliVersionSupport"/>）。</remarks>
    public string? MinimumVersion { get; init; }

    /// <summary>
    /// 这个部署接受的最高 CLI 版本（含），只比较写出来的那几段：<c>2.1</c> = 任何 2.1.x，<c>2</c> = 任何 2.x。空 = 不限。
    /// </summary>
    public string? MaximumVersion { get; init; }

    /// <summary>
    /// 部署的兜底令牌（<c>FallbackAuthToken</c>）是否也可以交给远程执行目标上的运行。缺省 false：
    /// 远程目标（用户的工作区容器）里的进程能读到自己的环境变量，把服务器的凭据送过去等于交给了在那里跑的人。
    /// </summary>
    public bool AllowFallbackAuthTokenOnRemoteTargets { get; init; }
}
