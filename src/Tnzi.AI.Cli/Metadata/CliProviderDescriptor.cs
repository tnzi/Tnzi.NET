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
    /// </remarks>
    public bool ResumeRejectionDetectable { get; init; }

    /// <summary>禁止用户 custom args 覆盖的参数（协议契约参数）。</summary>
    public IReadOnlyDictionary<string, BlockedArgMode> BlockedArgs { get; init; }
        = new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal);

    /// <summary>该 provider 是否必须内联投递 system prompt（无原生记忆文件时）。</summary>
    public bool RequiresInlineSystemPrompt { get; init; }

    /// <summary>本 provider 是否在当前部署启用（由 Options 合并时填充）。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>配置指定的可执行文件绝对路径。空 = 走 PATH 查找 <see cref="DefaultExecutable"/>。</summary>
    public string? ExecutablePathOverride { get; init; }

    /// <summary>部署级默认模型。</summary>
    public string? DefaultModel { get; init; }

    /// <summary>部署级默认追加参数，先于每 agent 的自定义参数。</summary>
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];
}
