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

    /// <summary>
    /// Where the conversation came from, when this message starts a new thread (<see cref="ThreadId"/> is null): stored on the
    /// thread as <c>AgentThreadDto.Origin</c>. Only values the application lets clients declare are accepted
    /// (<c>IThreadOriginPolicy</c>; by default <c>AI:Chat:AllowedThreadOrigins</c>), anything else is answered with
    /// 400 <c>AI_THREAD_ORIGIN_NOT_ALLOWED</c>. The application's policy may also replace it, or give an origin when none is
    /// declared, with one only the server knows. Not used when the message continues an existing thread.
    /// </summary>
    [MaxLength(ThreadOrigin.MaxLength)]
    public string? Origin { get; set; }

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

    /// <summary>
    /// When the owner sent this message, when that was earlier than now (server side only, never read from the wire):
    /// the thread's message queue sends a message queued earlier with the time it was queued, so the thread's place in the
    /// list (<c>AgentThreadDto.LastUserMessageTime</c>) does not move again when the queue delivers it. Null = now.
    /// </summary>
    [JsonIgnore]
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// Per-request reasoning effort for built-in agents (None = no reasoning). Not validated against the model list and not
    /// passed to external CLI runtimes; clients that offer a choice per model send <see cref="Effort"/> instead. When both are
    /// given on a built-in agent, this one wins.
    /// </summary>
    public ReasoningEffort? ReasoningEffort { get; set; }

    /// <summary>
    /// This turn's reasoning effort, a runtime-native value from the turn's model (<c>ChatModelDto.Efforts</c> of
    /// <c>GET chat/models</c>). A value the model does not offer to this user (or any value when the model offers no choice)
    /// is answered with 403 <c>AI_EFFORT_NOT_ALLOWED</c>. Null keeps the agent's own setting (the binding's
    /// <c>ThinkingLevel</c> on an external CLI runtime). On an external CLI runtime it is passed as <c>--effort</c>
    /// (stream-json) or the <c>thought_level</c> session option (ACP); on a built-in agent a value named like a
    /// <c>ReasoningEffort</c> member (none / low / medium / high / max) applies, other values are ignored.
    /// </summary>
    [MaxLength(64)]
    public string? Effort { get; set; }

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
/// A request of a run that still waits for the person attending the turn: a permission, the agent's questions, or its
/// plan. The fields are those of the <c>permission_request</c> stream event's <c>eventData</c>, plus the run it belongs
/// to and when it appeared. Answer at <c>POST chat/runs/{runId}/permissions/{requestId}</c>.
/// </summary>
public class ChatPermissionRequestDto
{
    /// <summary>The run that asks.</summary>
    public Guid RunId { get; set; }

    /// <summary>The id to answer with.</summary>
    public Guid RequestId { get; set; }

    /// <summary>What it waits for: <c>permission</c>, <c>question</c> or <c>plan</c>.</summary>
    public string Kind { get; set; } = CliInteractionKinds.Permission;

    /// <summary>Always <c>Pending</c> here.</summary>
    public string Status { get; set; } = nameof(CliPermissionStatus.Pending);

    /// <summary>The tool the agent wants to use.</summary>
    public string? Tool { get; set; }

    /// <summary>The tool call the request is about.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>The tool's input.</summary>
    public IReadOnlyDictionary<string, object?>? Input { get; set; }

    /// <summary>The agent's description of the action.</summary>
    public string? Description { get; set; }

    /// <summary>The CLI's title for the request.</summary>
    public string? Title { get; set; }

    /// <summary>Why the CLI asks, when it says.</summary>
    public string? Reason { get; set; }

    /// <summary>The path that triggered the request, when there is one.</summary>
    public string? BlockedPath { get; set; }

    /// <summary>Question requests: the questions, in the agent's order.</summary>
    public IReadOnlyList<CliQuestion>? Questions { get; set; }

    /// <summary>Plan requests: the plan (markdown).</summary>
    public string? Plan { get; set; }

    /// <summary>Where <see cref="Plan"/> came from (<c>CliPlanSources</c>).</summary>
    public string? PlanSource { get; set; }

    /// <summary>The answers it takes.</summary>
    public IReadOnlyList<CliPermissionOption> Options { get; set; } = [];

    /// <summary>After this moment (UTC) an unanswered request is denied.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>When the request appeared.</summary>
    public DateTime? CreatedAt { get; set; }
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
