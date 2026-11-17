namespace Tnzi.AI.Entities;

/// <summary>
/// 用户在线程的一轮还在跑时写下、在服务端排队的一条消息：交给正在跑的那个 CLI 进程（它在下一步之间收下），
/// 或者在那一轮结束时与其余等待的消息合成一轮发出。
/// </summary>
/// <remarks>
/// <para>
/// 队列在服务端，刷新页面不会丢，没有页面开着也照样发。正在跑的那一轮的进程收得下运行中的用户消息时（stream-json 的 claude，
/// 本机起的或远程宿主上的），执行它的服务端副本把消息写进进程（<see cref="QueuedMessageState.HandedOver"/>），CLI 收下时消息作为一条用户消息
/// 写进线程、排在 agent 收下它之前与之后的回答之间（<see cref="QueuedMessageState.Delivered"/>）；收不下的（ACP、
/// 带着文件的消息）等那一轮结束，作为新一轮发出。新一轮走 <c>IChatService</c>、以 <see cref="UserId"/> 的身份：Agent 策略、用量上限、
/// 允许的模型、元数据的限制与用户自己发消息时一样。
/// </para>
/// <para>
/// 作为新一轮发出的消息这一行就删除；交给进程并被收下的，在收下它的那一轮结束时删除。线程删除时连同它的排队消息一起清理。
/// 附件与聊天附件存在同一张表（<c>AgentAttachment</c>，<c>Origin = Upload</c>，挂在线程上、还没有消息），id 记在
/// <see cref="AttachmentIds"/>；消息写进线程时它们挂到那条消息上。
/// </para>
/// </remarks>
public class QueuedThreadMessage : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary><see cref="Model"/> 的最大长度。</summary>
    public const int ModelMaxLength = 200;

    /// <summary><see cref="Effort"/> 的最大长度。</summary>
    public const int EffortMaxLength = ChatModelEfforts.MaxLength;

    /// <summary><see cref="Failure"/> 的最大长度。</summary>
    public const int FailureMaxLength = 1000;

    /// <summary><see cref="FailureCode"/> 的最大长度。</summary>
    public const int FailureCodeMaxLength = 64;

    public Guid? TenantId { get; set; }

    /// <summary>所属线程。</summary>
    public Guid ThreadId { get; set; }

    /// <summary>线程主人：消息以这个用户的身份发出，也只有这个用户能列出、改、删、立即发送。</summary>
    public Guid UserId { get; set; }

    /// <summary>线程的 Agent，排队时记下，消息发给它；线程不绑 Agent 时为 null。</summary>
    public Guid? AgentId { get; set; }

    /// <summary>正文。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>输入框本来会随这条消息发出的每轮元数据（<c>ChatRequestDto.Metadata</c>，JSON 对象）；没有时为 null。</summary>
    public string? MetadataJson { get; set; }

    /// <summary>所选模型；null = 线程默认。发出时照常检查这个用户能不能用它。</summary>
    public string? Model { get; set; }

    /// <summary>所选推理强度（运行时原生值）；null = 不带。发出时照常按模型的可选档位检查。</summary>
    public string? Effort { get; set; }

    /// <summary>此刻的处境。</summary>
    public QueuedMessageState State { get; set; }

    /// <summary>
    /// 排队时线程上正在跑的那一轮（外部运行 id）：消息等它结束。没有在跑的一轮时为 null，消息当场发出。
    /// 「立即发送」之后，其余等待的消息改等那条消息启动的一轮。
    /// </summary>
    public Guid? AfterRunId { get; set; }

    /// <summary>用户要求立即单独发出：它排在最前，单独作为下一轮发出，其余消息等它启动的那一轮结束。</summary>
    public bool SendNow { get; set; }

    /// <summary>
    /// 扣住的原因：发出时被拒的说明（例如模型不允许、到了用量上限），或者它等的那一轮被停下了（<see cref="FailureCode"/> 为
    /// <c>AI_QUEUED_TURN_STOPPED</c>）。每一种扣住都写下原因，列表与页面只从这里读。
    /// </summary>
    public string? Failure { get; set; }

    /// <summary>与 <see cref="Failure"/> 同时写下的错误码（<c>ErrorCodes</c>），客户端据此显示本地化的说明。</summary>
    public string? FailureCode { get; set; }

    /// <summary>开始发出的时间（<see cref="QueuedMessageState.Delivering"/> 时才有）：服务在发出途中停掉时，扫描据此认出卡住的行。</summary>
    public DateTime? DeliveringSince { get; set; }

    /// <summary>
    /// 随它发出的附件（<c>AgentAttachment</c> 的 id，JSON 数组，按附上的先后）；没有时为 null。规则与聊天附件相同（<c>AI:Chat:Attachments</c>）。
    /// </summary>
    public string? AttachmentIds { get; set; }

    /// <summary>它交给了哪一轮的进程（<see cref="QueuedMessageState.HandedOver"/> 与 <see cref="QueuedMessageState.Delivered"/> 时才有）。</summary>
    public Guid? HandedToRunId { get; set; }

    /// <summary>交给进程的时间（UTC）。</summary>
    public DateTime? HandedOverAt { get; set; }

    /// <summary>agent 收下之后写进线程的那条用户消息（<see cref="QueuedMessageState.Delivered"/> 时才有）。</summary>
    public Guid? ThreadMessageId { get; set; }

    /// <summary>所属线程（导航）。</summary>
    public virtual AgentThread? Thread { get; set; }
}
