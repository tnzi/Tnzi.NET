namespace Tnzi.AI.Dtos;

/// <summary>
/// 聊天请求 DTO
/// </summary>
public class ChatRequestDto
{
    /// <summary>User message (text only, for simple requests)</summary>
    [MaxLength(10000)]
    public string? Message { get; set; }

    /// <summary>Multimodal content parts (text, image, file). Use this instead of Message for multimodal requests.</summary>
    public List<ContentPartDto>? Content { get; set; }

    /// <summary>Optional agent ID</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Optional thread ID for conversation</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>Provider name</summary>
    [MaxLength(50)]
    public string? Provider { get; set; }

    /// <summary>Model name</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>Tool group names for this request (optional; used when no AgentId)</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>User ID (for quota check)</summary>
    public Guid? UserId { get; set; }

    /// <summary>Per-request reasoning effort override (None = no reasoning)</summary>
    public ReasoningEffort? ReasoningEffort { get; set; }

    /// <summary>
    /// Edit: a user message of <see cref="ThreadId"/> that this message replaces. That message and everything after it
    /// are removed from the thread before the turn runs, and <see cref="Message"/> / <see cref="Content"/> are sent in
    /// its place. On an agent bound to an external CLI runtime the CLI session continues from the turn before it, so
    /// the replaced turns are gone from the agent's memory too.
    /// </summary>
    public Guid? EditMessageId { get; set; }

    /// <summary>
    /// Regenerate: send the last user message of <see cref="ThreadId"/> again (its text and attachments). The previous
    /// answer to it is removed first. <see cref="Message"/> and <see cref="Content"/> must be empty.
    /// </summary>
    public bool Regenerate { get; set; }

    /// <summary>
    /// 这一轮的每轮元数据：消费方自己定义的一小组值（键是字符串，值是任意 JSON），例如用户这条消息点名的几台设备的 id。
    /// </summary>
    /// <remarks>
    /// 框架不解释它，只校验大小（<c>AI:Chat:Metadata</c>：默认至多 16 个键、序列化后 4 KB，键字母开头、只含字母数字与 <c>_ . : -</c>，
    /// 不合规答 400 <c>AI_INVALID_TURN_METADATA</c>）。外部 CLI 路径上 <c>ICliAgentTurnHandler</c> 从
    /// <c>CliTurnContext.Metadata</c> 读到它；内建路径放在 <c>AgentRunRequest.TurnMetadata</c> 上。两条路径都随用户消息存下
    /// （<c>ThreadMessageDto.Metadata</c>）；重新生成没带它时沿用被重发那条消息存下的那份。
    /// </remarks>
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

/// <summary>
/// A run in progress behind a chat thread (a turn on an external CLI runtime), for a client to reattach to or cancel.
/// </summary>
public class ChatRunDto
{
    /// <summary>Run ID: reattach at <c>GET chat/runs/{runId}/stream</c>, cancel at <c>POST chat/runs/{runId}/cancel</c>.</summary>
    public Guid RunId { get; set; }

    /// <summary>Thread the run answers in.</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>The user message the run answers.</summary>
    public Guid? UserMessageId { get; set; }

    /// <summary>Run status: <c>Queued</c>, <c>Dispatched</c> or <c>Running</c>.</summary>
    public CliRunStatus Status { get; set; }

    /// <summary>When the run was queued.</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 聊天响应 DTO
/// </summary>
public class ChatResponseDto
{
    /// <summary>Response content</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>Finish reason</summary>
    public string? FinishReason { get; set; }
    /// <summary>Model used</summary>
    public string? Model { get; set; }
    /// <summary>Token usage</summary>
    public TokenUsageDto? Usage { get; set; }
    /// <summary>Thread ID</summary>
    public Guid? ThreadId { get; set; }
    /// <summary>Handoff path (only set when handoff execution mode was used)</summary>
    public List<string>? HandoffPath { get; set; }
    /// <summary>RAG citations (source documents used)</summary>
    public List<CitationDto>? Citations { get; set; }
    /// <summary>Reasoning/thinking content (populated in non-streaming mode)</summary>
    public string? Reasoning { get; set; }
    /// <summary>Persisted user message ID for this turn (null when persistence was skipped).</summary>
    public Guid? UserMessageId { get; set; }
    /// <summary>Persisted assistant message ID for this turn (null when persistence was skipped).</summary>
    public Guid? AssistantMessageId { get; set; }
}

/// <summary>
/// RAG 引用来源
/// </summary>
public class CitationDto
{
    /// <summary>Source document name</summary>
    public string? SourceName { get; set; }
    /// <summary>Source document link</summary>
    public string? SourceLink { get; set; }
    /// <summary>Referenced text excerpt</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Relevance score</summary>
    public double? Score { get; set; }
}
