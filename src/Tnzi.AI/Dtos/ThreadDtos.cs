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
    /// <summary>
    /// True while an AI title is being generated for the thread: <see cref="Title"/> is the fallback and is about to change.
    /// A client that shows the title can read the thread again shortly.
    /// </summary>
    public bool TitlePending { get; set; }
    /// <summary>
    /// Where the thread came from: a short value the application defines and writes when the thread is created
    /// (for example <c>Email</c> or <c>Schedule</c>); null when the application declared none. The framework passes it through as is.
    /// </summary>
    public string? Origin { get; set; }
    /// <summary>Message count</summary>
    public int MessageCount { get; set; }
    /// <summary>Last activity time</summary>
    public DateTime LastActivityTime { get; set; }
    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>When the thread was archived; null when it is not archived</summary>
    public DateTime? ArchivedTime { get; set; }
    /// <summary>
    /// Whether the thread shows in its owner's conversation lists. Written by server code only (<see cref="ThreadListing"/>);
    /// the user-facing list never returns a thread that is not <see cref="ThreadListing.Listed"/>.
    /// </summary>
    public ThreadListing Listing { get; set; }
    /// <summary>
    /// A turn of the thread is going on on the server right now: a run of an external agent that is queued or working
    /// (one that only waits on its background tasks after answering does not count). Turns of a built-in agent run on
    /// the request that started them and are not reported here.
    /// </summary>
    public bool Running { get; set; }
    /// <summary>
    /// A turn of the thread is blocked waiting for its owner: a permission request, a question or a plan of an external
    /// agent's live run that is still unanswered and unexpired, or anything an application declares through
    /// <c>IThreadAwaitingOwnerProvider</c> (a confirmation card of its own). The list puts these threads first.
    /// </summary>
    public bool AwaitingOwner { get; set; }
    /// <summary>
    /// When the owner last sent a message in the thread (UTC); null when they never did, and the list then uses
    /// <see cref="CreationTime"/>. A turn an application starts on the owner's behalf counts; replies, turn ends,
    /// renames and read marks do not. The list orders each tier by this time (see <c>ThreadListSort</c>).
    /// </summary>
    public DateTime? LastUserMessageTime { get; set; }
    /// <summary>
    /// A server-side turn wrote a reply into the thread after its owner last saw it (<c>POST threads/{id}/read</c>):
    /// the thread has something its owner has not read.
    /// </summary>
    public bool Unread { get; set; }
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
    /// <summary>
    /// Where the thread came from (<see cref="AgentThreadDto.Origin"/>): an application-defined value, stored as is.
    /// It starts with a letter and uses only letters, digits, <c>_ . : -</c>; null or empty = none.
    /// </summary>
    [MaxLength(ThreadOrigin.MaxLength)]
    public string? Origin { get; set; }
    /// <summary>
    /// Whether the new thread shows in its owner's conversation lists (<see cref="ThreadListing"/>). Default
    /// <see cref="ThreadListing.Listed"/>. Server code only: never read from a request body, so a client cannot create
    /// a thread kept out of the lists.
    /// </summary>
    [JsonIgnore]
    public ThreadListing Listing { get; set; } = ThreadListing.Listed;
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
    /// <summary>Start time filter: the owner's last message (creation when there is none) at or after</summary>
    public DateTime? StartTime { get; set; }
    /// <summary>End time filter: the owner's last message (creation when there is none) at or before</summary>
    public DateTime? EndTime { get; set; }
    /// <summary>Archive filter. Default <c>All</c> (no filter)</summary>
    public ThreadListStatus Status { get; set; } = ThreadListStatus.All;
    /// <summary>
    /// Order inside each tier of the list (threads awaiting their owner, then running ones, then the rest).
    /// Default <c>LastUserMessage</c> (the owner's most recent message first)
    /// </summary>
    public ThreadListSort Sort { get; set; } = ThreadListSort.LastUserMessage;
    /// <summary>
    /// Also return threads kept out of their owner's lists (<see cref="AgentThreadDto.Listing"/> other than
    /// <see cref="ThreadListing.Listed"/>). Default false. Set by server code only (the admin thread list sets it);
    /// never read from a request body.
    /// </summary>
    [JsonIgnore]
    public bool IncludeUnlisted { get; set; }
}

/// <summary>
/// 当前用户线程的关注状态查询 DTO
/// </summary>
public class ThreadAttentionQueryDto
{
    /// <summary>
    /// Agent ID set filter: only threads of these agents (as <see cref="ThreadListQueryDto.AgentIds"/>). Null or empty = no filter.
    /// </summary>
    [MaxLength(50)]
    public List<Guid>? AgentIds { get; set; }
    /// <summary>Creator ID filter (set by the user-facing API to the current user)</summary>
    [JsonIgnore]
    public Guid? CreatorId { get; set; }
}

/// <summary>
/// 当前用户线程的关注状态：哪些线程在等主人、哪些有一轮正在服务端跑、哪些有还没看过的回答
/// （与 <see cref="AgentThreadDto.AwaitingOwner"/> / <see cref="AgentThreadDto.Running"/> / <see cref="AgentThreadDto.Unread"/> 同一判定）。
/// </summary>
public class ThreadAttentionDto
{
    /// <summary>Most thread ids each list carries; the counts are exact beyond it</summary>
    public const int MaxThreadIds = 200;
    /// <summary>How many threads have a turn blocked waiting for their owner</summary>
    public int AwaitingOwnerCount { get; set; }
    /// <summary>How many threads have a turn going on on the server</summary>
    public int RunningCount { get; set; }
    /// <summary>How many threads have a reply their owner has not seen</summary>
    public int UnreadCount { get; set; }
    /// <summary>The threads waiting for their owner, in list order (at most <see cref="MaxThreadIds"/>)</summary>
    public List<Guid> AwaitingOwnerThreadIds { get; set; } = [];
    /// <summary>The threads with a turn going on, in list order (at most <see cref="MaxThreadIds"/>)</summary>
    public List<Guid> RunningThreadIds { get; set; } = [];
    /// <summary>The threads with an unseen reply, most recent reply first (at most <see cref="MaxThreadIds"/>)</summary>
    public List<Guid> UnreadThreadIds { get; set; } = [];
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
    /// <summary>
    /// True while an AI title is being generated for the thread: <see cref="Title"/> is the fallback and is about to change.
    /// A client that shows the title can read the thread again shortly.
    /// </summary>
    public bool TitlePending { get; set; }
    /// <summary>
    /// Where the thread came from: a short value the application defines and writes when the thread is created
    /// (for example <c>Email</c> or <c>Schedule</c>); null when the application declared none. The framework passes it through as is.
    /// </summary>
    public string? Origin { get; set; }
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
    /// <summary>
    /// Whether the thread shows in its owner's conversation lists. Written by server code only (<see cref="ThreadListing"/>);
    /// the user-facing list never returns a thread that is not <see cref="ThreadListing.Listed"/>.
    /// </summary>
    public ThreadListing Listing { get; set; }
    /// <summary>
    /// A turn of the thread is going on on the server right now: a run of an external agent that is queued or working
    /// (one that only waits on its background tasks after answering does not count). Turns of a built-in agent run on
    /// the request that started them and are not reported here.
    /// </summary>
    public bool Running { get; set; }
    /// <summary>
    /// A turn of the thread is blocked waiting for its owner: a permission request, a question or a plan of an external
    /// agent's live run that is still unanswered and unexpired, or anything an application declares through
    /// <c>IThreadAwaitingOwnerProvider</c> (a confirmation card of its own). The list puts these threads first.
    /// </summary>
    public bool AwaitingOwner { get; set; }
    /// <summary>
    /// When the owner last sent a message in the thread (UTC); null when they never did, and the list then uses
    /// <see cref="CreationTime"/>. A turn an application starts on the owner's behalf counts; replies, turn ends,
    /// renames and read marks do not. The list orders each tier by this time (see <c>ThreadListSort</c>).
    /// </summary>
    public DateTime? LastUserMessageTime { get; set; }
    /// <summary>
    /// A server-side turn wrote a reply into the thread after its owner last saw it (<c>POST threads/{id}/read</c>):
    /// the thread has something its owner has not read.
    /// </summary>
    public bool Unread { get; set; }
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
    /// <summary>
    /// Where the thread came from: a short value the application defines and writes when the thread is created
    /// (for example <c>Email</c> or <c>Schedule</c>); null when the application declared none. The framework passes it through as is.
    /// </summary>
    public string? Origin { get; set; }
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
