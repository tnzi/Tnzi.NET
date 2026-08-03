namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// 布置一次运行的工作区所需的全部输入。
/// </summary>
/// <remarks>
/// <b>刻意把上下文拆成两块</b>，因为它们进的位置不同、代价也不同：
/// <list type="bullet">
/// <item>
/// <see cref="StableBrief"/> 写进 provider 的原生记忆文件，位于整段对话之前 ——
/// 也就是 prompt 缓存前缀里。<b>每轮都变的内容绝不能进这里</b>，否则每次续接都作废
/// 整个历史的缓存，成本按整段上下文重算。
/// </item>
/// <item>
/// <see cref="PerTurnContext"/> 追加到本轮消息尾部，随便怎么变。
/// </item>
/// </list>
/// 约定测试 <c>WorkspaceBriefTests</c> 守住这条：同一 Agent 连续两次布置，
/// brief 文件必须逐字节相同。
/// </remarks>
public sealed record CliRunContext
{
    /// <summary>运行 ID。决定隔离目录名。</summary>
    public required Guid RunId { get; init; }

    /// <summary>Agent ID。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>租户。多租户部署下工作区按租户分区。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// 本次运行所属的会话线程。
    /// </summary>
    /// <remarks>
    /// 决定工作区按<b>线程</b>还是按<b>单次运行</b>划分，而这一条直接决定续接能否成立：
    /// 编码 CLI 把会话按<b>项目目录</b>存档（claude 存在 <c>~/.claude/projects/&lt;cwd 哈希&gt;/</c>），
    /// 每轮换一个新目录的话，上一轮的 session id 在新目录里根本不存在 ——
    /// resume 必然被拒，用户看到的就是 agent 完全不记得上一句话。
    /// <para>
    /// 同一线程共用目录还有第二个好处：agent 自己写下的记忆文件、笔记、半成品
    /// 能跨轮留存，这正是一个编码 agent 默认假设的工作方式。
    /// </para>
    /// </remarks>
    public Guid? ThreadId { get; init; }

    /// <summary>provider 描述（决定记忆文件名与 skills 目录）。</summary>
    public required CliProviderDescriptor Provider { get; init; }

    /// <summary>
    /// 写进 brief 文件的<b>稳定</b>内容。同一 Agent 多轮之间应逐字节相同。
    /// </summary>
    public string StableBrief { get; init; } = string.Empty;

    /// <summary>追加到本轮提示尾部的<b>易变</b>上下文。不写文件。</summary>
    public string? PerTurnContext { get; init; }

    /// <summary>工作目录策略。</summary>
    public CliWorkDirectoryMode WorkDirectoryMode { get; init; } = CliWorkDirectoryMode.PerThread;

    /// <summary><see cref="CliWorkDirectoryMode.UserProvided"/> 时的绝对路径。</summary>
    public string? UserWorkDirectory { get; init; }

    /// <summary>要物化到 provider 原生目录的技能。</summary>
    public IReadOnlyList<CliSkillPayload> Skills { get; init; } = [];

    /// <summary>受管 MCP 配置 JSON（Claude 风格 <c>mcpServers</c> 对象）。</summary>
    public string? McpConfigJson { get; init; }
}

/// <summary>
/// 一个待物化的技能。
/// </summary>
public sealed record CliSkillPayload
{
    /// <summary>技能标识（目录名）。</summary>
    public required string Slug { get; init; }

    /// <summary>用于路由的一句话描述，进 frontmatter。</summary>
    public string? Description { get; init; }

    /// <summary>技能正文。</summary>
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// 已布置好的工作区。
/// </summary>
public sealed record CliWorkspace
{
    /// <summary>运行根目录（隔离模式下就是 <c>{root}/{tenant}/{runId}</c>）。</summary>
    public required string RootDirectory { get; init; }

    /// <summary>agent 的 cwd。</summary>
    public required string WorkDirectory { get; init; }

    /// <summary>产物落点。</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>日志目录。</summary>
    public required string LogDirectory { get; init; }

    /// <summary>受管 MCP 配置文件路径；未配置时为 null。</summary>
    public string? McpConfigPath { get; init; }

    /// <summary>本次布置创建的文件/目录清单（回滚依据）。</summary>
    public IReadOnlyList<string> Sidecars { get; init; } = [];

    /// <summary>工作目录是否属于用户（属于则<b>永不删除</b>）。</summary>
    public bool WorkDirectoryIsUserOwned { get; init; }
}
