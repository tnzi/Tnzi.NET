namespace Tnzi.AI.Dtos;

/// <summary>
/// Agent 线程 DTO
/// </summary>
public class AgentThreadDto
{
    /// <summary>Thread ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID (null for agent-less threads)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Agent name (null for agent-less threads or when the agent was deleted)</summary>
    public string? AgentName { get; set; }
    /// <summary>Thread title</summary>
    public string? Title { get; set; }
    /// <summary>Message count</summary>
    public int MessageCount { get; set; }
    /// <summary>Last activity time</summary>
    public DateTime LastActivityTime { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>When the thread was archived; null when it is not archived</summary>
    public DateTime? ArchivedTime { get; set; }
}

/// <summary>
/// 创建线程 DTO
/// </summary>
public class CreateAgentThreadDto
{
    /// <summary>Agent ID (null for agent-less threads)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Thread title</summary>
    [MaxLength(200)]
    public string? Title { get; set; }
}

/// <summary>
/// 线程列表查询 DTO
/// </summary>
public class ThreadListQueryDto : PagedQueryDto
{
    /// <summary>Agent ID filter</summary>
    public Guid? AgentId { get; set; }
    /// <summary>
    /// Agent ID set filter: only threads of these agents. Null or empty = no filter. Combined with
    /// <see cref="AgentId"/> by AND; agent-less threads never match a non-empty set.
    /// </summary>
    [MaxLength(50)]
    public List<Guid>? AgentIds { get; set; }
    /// <summary>Creator ID filter (used by user-facing API for ownership scoping)</summary>
    [JsonIgnore]
    public Guid? CreatorId { get; set; }
    /// <summary>Keyword search (title)</summary>
    [MaxLength(100)]
    public string? Keyword { get; set; }
    /// <summary>Start time filter (last activity at or after)</summary>
    public DateTime? StartTime { get; set; }
    /// <summary>End time filter (last activity at or before)</summary>
    public DateTime? EndTime { get; set; }
    /// <summary>Archive filter. Default <c>All</c> (no filter)</summary>
    public ThreadListStatus Status { get; set; } = ThreadListStatus.All;
    /// <summary>Order of the list. Default <c>LastActivity</c> (most recent first)</summary>
    public ThreadListSort Sort { get; set; } = ThreadListSort.LastActivity;
}

/// <summary>
/// 更新线程标题请求 DTO
/// </summary>
public class UpdateThreadTitleDto
{
    /// <summary>New title</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;
}

/// <summary>
/// 线程详情 DTO（含最近消息）
/// </summary>
public class AgentThreadDetailDto
{
    /// <summary>Thread ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID (null for agent-less threads)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Agent name</summary>
    public string? AgentName { get; set; }
    /// <summary>Thread title</summary>
    public string? Title { get; set; }
    /// <summary>Thread metadata (JSON)</summary>
    public string? Metadata { get; set; }
    /// <summary>Total message count</summary>
    public int MessageCount { get; set; }
    /// <summary>Last activity time</summary>
    public DateTime LastActivityTime { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>When the thread was archived; null when it is not archived</summary>
    public DateTime? ArchivedTime { get; set; }
    /// <summary>Recent messages</summary>
    public List<ThreadMessageDto> Messages { get; set; } = [];
}

/// <summary>
/// 线程消息 DTO
/// </summary>
public class ThreadMessageDto
{
    /// <summary>Message ID</summary>
    public Guid Id { get; set; }
    /// <summary>Role (system, user, assistant, tool)</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Message content</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>Tool calls (JSON)</summary>
    public string? ToolCalls { get; set; }
    /// <summary>Token usage (JSON)</summary>
    public string? Usage { get; set; }
    /// <summary>Message order</summary>
    public int Order { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Component ref IDs (JSON array of GUIDs)</summary>
    public string? ComponentRefs { get; set; }
    /// <summary>Feedback rating: true=👍, false=👎, null=not rated</summary>
    public bool? FeedbackRating { get; set; }
    /// <summary>Feedback tags (JSON array, for negative feedback)</summary>
    public string? FeedbackTags { get; set; }
    /// <summary>Feedback comment</summary>
    public string? FeedbackComment { get; set; }
    /// <summary>Feedback submission time</summary>
    public DateTime? FeedbackTime { get; set; }
    /// <summary>
    /// How the reply ended when it did not simply complete: <c>stopped</c>, <c>failed</c> or <c>usage_limit</c> (null otherwise).
    /// </summary>
    public string? Status { get; set; }
    /// <summary>
    /// When <see cref="Status"/> is <c>usage_limit</c>: when the external CLI's account limit resets (UTC), if the CLI said.
    /// </summary>
    public DateTime? UsageLimitResetsAt { get; set; }
    /// <summary>
    /// On a user message: the per-turn metadata the client sent with it (<c>ChatRequestDto.Metadata</c>); null when none.
    /// </summary>
    public Dictionary<string, JsonElement>? Metadata { get; set; }
    /// <summary>
    /// Files shown on the message: what the user attached, and on a reply the images its tool calls returned
    /// (those are also referenced from the tool call results by id).
    /// </summary>
    public List<AttachmentDto>? Attachments { get; set; }
}

/// <summary>
/// 一条线程消息的定位信息（编辑与重新生成用）。
/// </summary>
/// <param name="Id">消息 id。</param>
/// <param name="Order">消息在线程里的顺序。</param>
/// <param name="Content">消息正文。</param>
public sealed record ThreadMessageRef(Guid Id, int Order, string Content)
{
    /// <summary>这条消息存下的每轮元数据（JSON 对象）；没有时为 null。</summary>
    public string? MetadataJson { get; init; }
}

/// <summary>
/// 一页线程消息（往回翻页）。
/// </summary>
public class ThreadMessagePageDto
{
    /// <summary>Messages of this page, oldest first.</summary>
    public List<ThreadMessageDto> Messages { get; set; } = [];

    /// <summary>Whether there are older messages before the first one of this page.</summary>
    public bool HasMore { get; set; }
}

/// <summary>
/// 线程导出 DTO（完整线程数据，含所有消息）
/// </summary>
public class ThreadExportDto
{
    /// <summary>Thread ID</summary>
    public Guid Id { get; set; }
    /// <summary>Agent ID (null for agent-less threads)</summary>
    public Guid? AgentId { get; set; }
    /// <summary>Agent name</summary>
    public string? AgentName { get; set; }
    /// <summary>Thread title</summary>
    public string? Title { get; set; }
    /// <summary>Thread metadata (JSON)</summary>
    public string? Metadata { get; set; }
    /// <summary>Total message count</summary>
    public int MessageCount { get; set; }
    /// <summary>Last activity time</summary>
    public DateTime LastActivityTime { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Export time</summary>
    public DateTime ExportedAt { get; set; }
    /// <summary>All messages (ordered)</summary>
    public List<ThreadMessageDto> Messages { get; set; } = [];
}
