namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// 流式事件（delta 模型 — 每个事件只包含增量内容，非累积）
/// </summary>
public class StreamEvent
{
    /// <summary>增量文本（非累积内容）</summary>
    public string? Delta { get; set; }

    /// <summary>完成原因 (stop, length, tool_calls, etc.)</summary>
    public string? FinishReason { get; set; }

    /// <summary>使用的模型</summary>
    public string? Model { get; set; }

    /// <summary>线程 ID</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>Token 使用量（仅在最终事件中包含）</summary>
    public TokenUsageDto? Usage { get; set; }

    /// <summary>是否为终止事件</summary>
    public bool IsDone { get; set; }

    /// <summary>是否为错误事件</summary>
    public bool IsError { get; set; }

    /// <summary>错误消息（仅在 IsError 为 true 时包含）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>错误码（仅在 IsError 为 true 时包含）</summary>
    public string? ErrorCode { get; set; }

    /// <summary>是否为工具调用中间状态（用于心跳）</summary>
    public bool IsToolCall { get; set; }

    /// <summary>Tool names being called (sent with IsToolCall = true for early UI feedback)</summary>
    public List<string>? ToolCallNames { get; set; }

    /// <summary>推理增量文本（DeepSeek-R1 等模型的思考过程，如 reasoning_content）</summary>
    public string? ReasoningDelta { get; set; }

    /// <summary>Agent 名称变更（Handoff 场景：通知前端当前 Agent 已切换）</summary>
    public string? AgentName { get; set; }

    /// <summary>RAG 引用来源（仅在 IsDone=true 时包含）</summary>
    public List<CitationDto>? Citations { get; set; }

    /// <summary>工作流执行阶段（routing, executing, reviewing, synthesizing, awaiting_approval）</summary>
    public string? Phase { get; set; }

    /// <summary>节点类型（agent, review, approval, router, parallel, ...）</summary>
    public string? NodeType { get; set; }

    /// <summary>工作流步骤 ID</summary>
    public string? NodeId { get; set; }

    /// <summary>步骤名称（用于 UI 展示）</summary>
    public string? NodeName { get; set; }

    /// <summary>执行该节点的 Agent 名称</summary>
    public string? WorkerName { get; set; }

    /// <summary>审查结论（accept, rework, reject）</summary>
    public string? ReviewVerdict { get; set; }

    /// <summary>是否等待人工审批</summary>
    public bool AwaitingApproval { get; set; }

    /// <summary>关联的 Run ID</summary>
    public Guid? RunId { get; set; }

    /// <summary>Rich component reference for frontend interactive rendering</summary>
    public ComponentRefDto? ComponentRef { get; set; }

    /// <summary>Tool call details (name + duration, for client-side benchmarking)</summary>
    public List<ToolCallDetail>? ToolCalls { get; set; }

    /// <summary>结构化事件类型（如 clarification、todos_updated、sub_agent_started）</summary>
    public string? EventType { get; set; }

    /// <summary>结构化事件附加数据</summary>
    public Dictionary<string, object>? EventData { get; set; }

    /// <summary>后续建议问题</summary>
    public List<string>? Suggestions { get; set; }

    /// <summary>Todo 列表快照</summary>
    public List<TodoItemDto>? Todos { get; set; }

    /// <summary>运行期间产出的文件产物</summary>
    public List<AgentArtifactDto>? Artifacts { get; set; }

    /// <summary>
    /// Persisted user message ID (populated on the terminal event when the
    /// turn is persisted; null when persistence was skipped, e.g. guardrail rejection).
    /// </summary>
    public Guid? UserMessageId { get; set; }

    /// <summary>
    /// Persisted assistant message ID (populated on the terminal event when the
    /// turn is persisted; null when persistence was skipped).
    /// Consumers use this to call message-scoped APIs such as feedback submission.
    /// </summary>
    public Guid? AssistantMessageId { get; set; }
}
