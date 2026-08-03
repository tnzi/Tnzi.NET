namespace Tnzi.AI.Dtos;

/// <summary>
/// 一台宿主上一个可用的外部 agent CLI（读模型）。
/// </summary>
public class CliRuntimeDto
{
    /// <summary>运行时 ID</summary>
    public Guid Id { get; set; }
    /// <summary>宿主标识（进程内 = 机器名；远程 daemon = daemon 自报 ID）</summary>
    public string HostId { get; set; } = string.Empty;
    /// <summary>provider 键，对应描述表，如 "claude"</summary>
    public string ProviderKey { get; set; } = string.Empty;
    /// <summary>provider 展示名，如 "Claude Code"</summary>
    public string? ProviderDisplayName { get; set; }
    /// <summary>协议族名（stream-json / acp / vendor-app-server）</summary>
    public string? Protocol { get; set; }
    /// <summary>展示名</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>探测到的可执行文件绝对路径</summary>
    public string ExecutablePath { get; set; } = string.Empty;
    /// <summary>探测到的 CLI 版本。仅供观测，绝不用于选择行为分支</summary>
    public string? CliVersion { get; set; }
    /// <summary>执行位置</summary>
    public CliRuntimeMode Mode { get; set; }
    /// <summary>可用状态</summary>
    public CliRuntimeStatus Status { get; set; }
    /// <summary>最近心跳</summary>
    public DateTime? LastSeenAt { get; set; }
    /// <summary>宿主信息 JSON（OS、架构等），仅供展示</summary>
    public string? HostInfoJson { get; set; }
    /// <summary>本 runtime 最大并发运行数</summary>
    public int MaxConcurrentRuns { get; set; }
    /// <summary>用户可见的启动骨架预览，如 "claude (stream-json)"</summary>
    public string? LaunchHeader { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 管理端可改的运行时字段。探测得来的路径/版本不在此列 —— 那些是观测结果，不是配置。
/// </summary>
public class UpdateCliRuntimeDto
{
    /// <summary>展示名</summary>
    public string? Name { get; set; }
    /// <summary>可用状态（仅允许在 Online / Disabled 间切换）</summary>
    public CliRuntimeStatus? Status { get; set; }
    /// <summary>最大并发运行数</summary>
    public int? MaxConcurrentRuns { get; set; }
}

/// <summary>
/// Agent 与外部运行时的绑定。<b>绑定存在即代表该 Agent 走外部执行。</b>
/// </summary>
public class CliAgentBindingDto
{
    /// <summary>绑定 ID</summary>
    public Guid Id { get; set; }
    /// <summary>框架 Agent ID</summary>
    public Guid AgentId { get; set; }
    /// <summary>外部运行时 ID</summary>
    public Guid CliRuntimeId { get; set; }
    /// <summary>运行时展示名（投影，便于列表直接渲染）</summary>
    public string? CliRuntimeName { get; set; }
    /// <summary>provider 键（投影）</summary>
    public string? ProviderKey { get; set; }
    /// <summary>模型覆盖。空 = 用 CLI 自己的默认</summary>
    public string? Model { get; set; }
    /// <summary>运行时原生的推理强度值，原样往返。空 = CLI 默认</summary>
    public string? ThinkingLevel { get; set; }
    /// <summary>每 agent 自定义 CLI 参数</summary>
    public List<string>? CustomArgs { get; set; }
    /// <summary>受管 MCP 配置 JSON。null = 继承宿主本机配置</summary>
    public string? McpConfigJson { get; set; }
    /// <summary>工作目录策略</summary>
    public CliWorkDirectoryMode WorkDirectoryMode { get; set; }
    /// <summary>WorkDirectoryMode = UserProvided 时的绝对路径</summary>
    public string? UserWorkDirectory { get; set; }
    /// <summary>是否把 Agent 的 SystemPrompt 写进 brief</summary>
    public bool InjectAgentInstructions { get; set; }
    /// <summary>是否把 Agent 授予的 skills 物化到 provider 原生目录</summary>
    public bool MaterializeSkills { get; set; }
    /// <summary>本 agent 的空闲看门狗覆盖（只允许收紧，不允许放宽）</summary>
    public TimeSpan? IdleWatchdog { get; set; }
}

/// <summary>
/// 新建或更新一条 Agent → 运行时绑定。
/// </summary>
public class UpsertCliAgentBindingDto
{
    /// <summary>外部运行时 ID</summary>
    public Guid CliRuntimeId { get; set; }
    /// <summary>模型覆盖</summary>
    public string? Model { get; set; }
    /// <summary>运行时原生推理强度值</summary>
    public string? ThinkingLevel { get; set; }
    /// <summary>每 agent 自定义 CLI 参数</summary>
    public List<string>? CustomArgs { get; set; }
    /// <summary>受管 MCP 配置 JSON</summary>
    public string? McpConfigJson { get; set; }
    /// <summary>工作目录策略</summary>
    public CliWorkDirectoryMode WorkDirectoryMode { get; set; } = CliWorkDirectoryMode.PerThread;
    /// <summary>UserProvided 时的绝对路径</summary>
    public string? UserWorkDirectory { get; set; }
    /// <summary>是否注入 Agent 指令到 brief</summary>
    public bool InjectAgentInstructions { get; set; } = true;
    /// <summary>是否物化 skills</summary>
    public bool MaterializeSkills { get; set; } = true;
    /// <summary>空闲看门狗覆盖（只收紧）</summary>
    public TimeSpan? IdleWatchdog { get; set; }
}

/// <summary>
/// 入队一次外部执行。
/// </summary>
public class CliRunRequestDto
{
    /// <summary>要执行的 Agent（必须已有绑定）</summary>
    public Guid AgentId { get; set; }
    /// <summary>提示词</summary>
    public string Prompt { get; set; } = null!;
    /// <summary>关联对话线程（聊天场景）</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>关联的框架 AgentRun，让外部执行也出现在统一 Run 视图里</summary>
    public Guid? AgentRunId { get; set; }
    /// <summary>队列优先级，大者先</summary>
    public int Priority { get; set; }
    /// <summary>
    /// 追加到本轮提示尾部的易变上下文（触发者、上一轮是否丢失上下文等）。
    /// <b>不要</b>把它写进 brief —— brief 位于缓存前缀，每轮变化会作废整段历史的 prompt cache。
    /// </summary>
    public string? PerTurnContext { get; set; }
    /// <summary>发起用户</summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// 一次外部执行的记录。
/// </summary>
public class CliRunDto
{
    /// <summary>Run ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }
    /// <summary>运行时 ID</summary>
    public Guid CliRuntimeId { get; set; }
    /// <summary>provider 键（投影）</summary>
    public string? ProviderKey { get; set; }
    /// <summary>关联的框架 AgentRun</summary>
    public Guid? AgentRunId { get; set; }
    /// <summary>关联对话线程</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>状态</summary>
    public CliRunStatus Status { get; set; }
    /// <summary>优先级</summary>
    public int Priority { get; set; }
    /// <summary>提示词</summary>
    public string Prompt { get; set; } = string.Empty;
    /// <summary>最终交付物</summary>
    public string? Output { get; set; }
    /// <summary>错误信息</summary>
    public string? Error { get; set; }
    /// <summary>失败分类</summary>
    public CliRunFailureReason? FailureReason { get; set; }
    /// <summary>provider 侧会话 ID</summary>
    public string? ProviderSessionId { get; set; }
    /// <summary>工作目录绝对路径</summary>
    public string? WorkDirectory { get; set; }
    /// <summary>认领时间</summary>
    public DateTime? DispatchedAt { get; set; }
    /// <summary>开始执行时间</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>结束时间</summary>
    public DateTime? CompletedAt { get; set; }
    /// <summary>耗时（毫秒）</summary>
    public long DurationMs { get; set; }
    /// <summary>按模型分组的用量 JSON</summary>
    public string? UsageJson { get; set; }
    /// <summary>估算成本（USD）</summary>
    public decimal? EstimatedCostUsd { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 外部执行的一条持久化事件。
/// </summary>
public class CliRunMessageDto
{
    /// <summary>消息 ID</summary>
    public Guid Id { get; set; }
    /// <summary>所属 Run</summary>
    public Guid RunId { get; set; }
    /// <summary>运行内单调递增序号。断线重连按它补发</summary>
    public int Sequence { get; set; }
    /// <summary>事件类型</summary>
    public CliAgentEventType Type { get; set; }
    /// <summary>文本内容</summary>
    public string? Content { get; set; }
    /// <summary>工具名</summary>
    public string? Tool { get; set; }
    /// <summary>工具调用 ID</summary>
    public string? CallId { get; set; }
    /// <summary>工具入参 JSON</summary>
    public string? InputJson { get; set; }
    /// <summary>工具输出（超长截断）</summary>
    public string? Output { get; set; }
    /// <summary>状态标识</summary>
    public string? Status { get; set; }
    /// <summary>日志级别</summary>
    public string? Level { get; set; }
    /// <summary>产生时间</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 外部执行记录查询条件。
/// </summary>
public class CliRunQueryDto : PagedQueryDto
{
    /// <inheritdoc />
    protected override int DefaultPageSize => 20;

    /// <summary>按 Agent 过滤</summary>
    public Guid? AgentId { get; set; }
    /// <summary>按运行时过滤</summary>
    public Guid? CliRuntimeId { get; set; }
    /// <summary>按状态过滤</summary>
    public CliRunStatus? Status { get; set; }
    /// <summary>按线程过滤</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>起始时间（含）</summary>
    public DateTime? StartTime { get; set; }
    /// <summary>结束时间（含）</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 一个可用的外部 agent provider 描述（面向管理端下拉）。
/// </summary>
public class CliProviderOptionDto
{
    /// <summary>provider 键</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>展示名</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>协议族名</summary>
    public string Protocol { get; set; } = string.Empty;
    /// <summary>默认可执行文件名</summary>
    public string DefaultExecutable { get; set; } = string.Empty;
    /// <summary>启动骨架预览</summary>
    public string? LaunchHeader { get; set; }
    /// <summary>本部署是否启用</summary>
    public bool Enabled { get; set; }
    /// <summary>该协议是否已有适配器实现（描述表里存在不等于可用）</summary>
    public bool Implemented { get; set; }
}

/// <summary>
/// 一次探测的结果摘要。
/// </summary>
public class CliRuntimeProbeResultDto
{
    /// <summary>本次探测到并已注册/更新的运行时</summary>
    public List<CliRuntimeDto> Runtimes { get; set; } = [];
    /// <summary>探测过但 PATH 上没找到的 provider 键</summary>
    public List<string> NotFound { get; set; } = [];
}
