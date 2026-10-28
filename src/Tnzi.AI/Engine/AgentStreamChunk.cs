
namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 流式执行的增量块
/// </summary>
public class AgentStreamChunk
{
    /// <summary>
    /// 增量文本内容
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// 本 chunk 携带的 LLM 调用用量<b>增量</b>。一次运行会出现多个（工具循环每轮一个，多 Agent 策略每个参与者各自的），
    /// 消费方必须逐个相加（<see cref="StreamUsage.Add"/>），不能取最后一个。
    /// </summary>
    public TokenUsageDto? Usage { get; set; }

    /// <summary>
    /// 结束原因（通常只在最后一个 chunk 中出现）
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// 当前 chunk 对应的实际执行模型
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 是否为工具调用中间状态（此时 Text 可能为 null）
    /// </summary>
    public bool IsToolCall { get; set; }

    /// <summary>
    /// Tool names being called (sent alongside IsToolCall = true for early UI feedback)
    /// </summary>
    public List<string>? ToolCallNames { get; set; }

    /// <summary>
    /// 错误信息（工具调用超时、Guardrail 拒绝等）
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 推理/思考增量文本（来自 DeepSeek-R1 等模型的 reasoning_content）
    /// </summary>
    public string? ReasoningText { get; set; }

    /// <summary>
    /// Agent 名称变更（Handoff 场景：当前对话切换到新 Agent 时设置）
    /// </summary>
    public string? AgentName { get; set; }

    /// <summary>
    /// RAG 引用来源（仅在最终 chunk 中包含）
    /// </summary>
    public List<CitationDto>? Citations { get; set; }

    /// <summary>
    /// Tool call details (name + duration, sent after each tool execution batch)
    /// </summary>
    public List<ToolCallDetail>? ToolCalls { get; set; }

    /// <summary>
    /// 流式输出粒度模式（标记此 chunk 属于哪种模式）
    /// </summary>
    public StreamMode Mode { get; set; } = StreamMode.Messages;

    /// <summary>
    /// 事件类型（Steps/Debug 模式下的具体事件标识）
    /// </summary>
    public string? EventType { get; set; }

    /// <summary>
    /// 事件附加数据
    /// </summary>
    public Dictionary<string, object>? EventData { get; set; }

    /// <summary>后续建议问题。框架的运行管线不填它（恒为 null），<see cref="StreamMode.Suggestion"/> 目前不产生任何事件</summary>
    public List<string>? Suggestions { get; set; }

    /// <summary>Todo 列表快照（Plan Mode 中间状态更新）</summary>
    public List<TodoItemDto>? Todos { get; set; }

    /// <summary>产出的文件产物（最终 chunk 中包含）</summary>
    public List<AgentArtifactDto>? Artifacts { get; set; }

    /// <summary>
    /// Persisted user message ID (populated on the terminal chunk when the turn is persisted).
    /// HistoryMiddleware sets this before yielding so downstream consumers (e.g. ChatService)
    /// can surface it on the SSE event without relying on AsyncLocal back-propagation.
    /// </summary>
    public Guid? UserMessageId { get; set; }

    /// <summary>
    /// Persisted assistant message ID (populated on the terminal chunk).
    /// Lets clients call message-scoped APIs (e.g. feedback) using the real DB id.
    /// </summary>
    public Guid? AssistantMessageId { get; set; }

    /// <summary>
    /// 承载这一轮的运行（外部 CLI 运行的 id）。外部路径在第一个块（<see cref="RunStreamEventTypes.RunStarted"/>）
    /// 与终止块上给出，客户端据此重新接上（刷新、断线）或取消它。内建路径不填。
    /// </summary>
    public Guid? RunId { get; set; }

    /// <summary>
    /// 这个块来自运行的第几条事件（外部 CLI 运行）。重新接上时把收到的最后一个序号带回去，从它之后接着收。
    /// </summary>
    public int? Sequence { get; set; }

    /// <summary>
    /// 带 <see cref="Error"/> 的块的业务错误码；为 null 时按结束原因取通用码（例如 <c>AI_CHAT_FAILED</c>）。
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 错误码为 <c>AI_USAGE_LIMIT_REACHED</c> 或 <c>AI_CLI_USAGE_LIMIT</c> 时，用量上限的重置时间（UTC）；不知道时为 null。
    /// </summary>
    public DateTime? UsageLimitResetsAt { get; set; }

    /// <summary>
    /// 错误码为 <c>AI_USAGE_LIMIT_REACHED</c> 时，到上限的是哪个窗口。
    /// </summary>
    public UsageLimitWindow? UsageLimitWindow { get; set; }

    /// <summary>
    /// 浅拷贝并替换文本，其余字段原样（本实例不修改）。
    /// </summary>
    internal AgentStreamChunk WithText(string? text)
    {
        var copy = (AgentStreamChunk)MemberwiseClone();
        copy.Text = text;
        return copy;
    }
}
