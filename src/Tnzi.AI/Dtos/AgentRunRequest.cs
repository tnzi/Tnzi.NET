namespace Tnzi.AI.Dtos;

/// <summary>
/// AI 运行请求
/// </summary>
public class AgentRunRequest
{
    /// <summary>请求操作类型（用于 usage analytics 分类）</summary>
    public string? OperationType { get; init; }

    /// <summary>Agent ID（与 Provider/Model 二选一）</summary>
    public Guid? AgentId { get; init; }

    /// <summary>直接指定 Provider（无需预定义 Agent）</summary>
    public string? Provider { get; init; }

    /// <summary>直接指定 Model</summary>
    public string? Model { get; init; }

    /// <summary>用户消息（文本或多模态）</summary>
    public string? UserMessage { get; init; }

    /// <summary>多模态内容部分</summary>
    public List<ContentPartDto>? ContentParts { get; init; }

    /// <summary>对话线程 ID（为空则新建，中间件可回写）</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>附加工具组</summary>
    public List<string>? ToolGroups { get; init; }

    /// <summary>
    /// 附加的单个工具名称（per-request per-tool override，与 <see cref="ToolGroups"/> 对称叠加）。
    /// 在工具组解析之外额外解析这些命名工具并按名称去重合并；权限仍然门控访问。
    /// Per-request individual tool names - additive to <see cref="ToolGroups"/>, symmetric override.
    /// Resolved in addition to group-expanded tools and unioned (deduped by name); permissions still gate access.
    /// </summary>
    public List<string>? ToolNames { get; init; }

    /// <summary>
    /// 本请求的 <see cref="ToolGroups"/> / <see cref="ToolNames"/> 由进程内代码选定（应用代码、管理员模板），
    /// 不受面向 HTTP 的 <c>AI:AdHocTools</c> 允许列表约束。
    /// ★ 只能由进程内调用方置 true，<b>绝不映射任何 HTTP 请求体</b>：<c>ChatRequestDto</c> 没有这个字段，
    /// 面向客户端的服务构造请求时保持默认 false，无 AgentId 的请求自选的组必须逐个在允许列表里，否则 403。
    /// </summary>
    public bool TrustedToolSelection { get; init; }

    /// <summary>Workflow 定义 ID（若指定则走 Workflow 模式）</summary>
    public Guid? WorkflowId { get; init; }

    /// <summary>Workflow 输入变量</summary>
    public Dictionary<string, object>? WorkflowInputs { get; init; }

    /// <summary>是否创建 Run 记录（用于追踪复杂运行）</summary>
    public bool EnableRunTracking { get; init; }

    /// <summary>复用已有 Run 记录（后台启动场景）</summary>
    public Guid? ExistingRunId { get; init; }

    /// <summary>
    /// 本请求是 <c>ISubAgentExecutionService.SpawnAsync</c> 起的后台运行。
    /// </summary>
    /// <remarks>
    /// 「后台运行」的判据必须来自请求本身，且不能是 <see cref="ParentRunId"/>：管理端 spawn 端点与
    /// 未开运行追踪的聊天里的 <c>spawn_agent</c> 起的都是没有父运行的根 spawn，按 ParentRunId 认
    /// 会让它们逃过 <c>AsyncAgentAllowedTools</c> 白名单与子 Agent 标记。
    /// ★ 只由进程内的 spawn 路径置 true，<b>绝不映射任何 HTTP 请求体</b>（与 <see cref="TrustedToolSelection"/> 同一纪律）。
    /// </remarks>
    public bool IsBackground { get; init; }

    /// <summary>父级 Run ID（子 Agent / workflow 子调用）</summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>根 Run ID（整条调用链共享）</summary>
    public Guid? RootRunId { get; init; }

    /// <summary>
    /// 子 Agent 名称（起子 Agent 时的类型名）。
    /// </summary>
    /// <remarks>
    /// 后台起的子 Agent 在新作用域、新执行流里跑，父级的属性包不会流过去，
    /// 名字只能随请求一起传。<c>AgentRuntime</c> 会连同 <see cref="ParentRunId"/> 一起
    /// 翻译成执行上下文里的 <c>ContextPropertyKeys.IsSubAgent</c> / <c>SubAgentName</c>。
    /// </remarks>
    public string? SubAgentName { get; init; }

    /// <summary>当前用户 ID（用于配额检查等）</summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// 钉住的 Agent 版本号：解析时加载该版本的配置快照（含快照里的资源授权）而不是活行，也不再经 A/B 路由。
    /// 只由框架内部路径（评估）设置，**刻意不映射任何 HTTP 请求体**：版本快照里的工具授权可能比活行宽。
    /// </summary>
    public int? AgentVersionNumber { get; init; }

    /// <summary>
    /// 临时运行：ThreadResolutionMiddleware 与 HistoryMiddleware 都不建线程，History 不读历史、不落库。
    /// 评估用例逐条跑在评估者名下，不该给每个用例留下一条 AgentThread（也不该各开一份 ThreadData / 沙箱）。
    /// </summary>
    public bool Ephemeral { get; init; }

    /// <summary>Per-request reasoning effort override (None = no reasoning)</summary>
    public ReasoningEffort? ReasoningEffort { get; init; }

    /// <summary>文件附件列表（由 FileUploadMiddleware 处理）</summary>
    public List<FileAttachment>? Attachments { get; init; }

    /// <summary>扩展元数据</summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>是否启用计划模式（启用 Todo 追踪）</summary>
    public bool PlanMode { get; init; }

    /// <summary>流式输出粒度模式（默认仅消息文本流）</summary>
    public StreamMode StreamMode { get; set; } = StreamMode.Messages;
}
