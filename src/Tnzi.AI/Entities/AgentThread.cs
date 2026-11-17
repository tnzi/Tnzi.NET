namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 会话线程实体
/// </summary>
public class AgentThread : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// Agent ID（可空：ToolGroups 模式下无关联 Agent）
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// Agent
    /// </summary>
    public virtual Agent? Agent { get; set; }

    /// <summary>
    /// 线程标题
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 标题从哪来；<see cref="ThreadTitleSource.User"/> 表示用户改过名，自动标题不再替换它。
    /// </summary>
    public ThreadTitleSource TitleSource { get; set; }

    /// <summary>
    /// 线程从哪来：应用自己定义的一个短值（例如 <c>Email</c>、<c>Schedule</c>），建线程时由应用写入，之后不再改变。
    /// null = 应用没有声明（手动开始的普通对话）。框架不解释它，只校验形状（见 <see cref="ThreadOrigin"/>）
    /// 并在线程 DTO 与列表里原样带出。
    /// </summary>
    public string? Origin { get; set; }

    /// <summary>
    /// 线程在不在主人的对话列表里；默认 <see cref="ThreadListing.Listed"/>。只有服务端代码写它（见 <see cref="ThreadListing"/>）：
    /// <see cref="ThreadListing.UntilUserMessage"/> 的线程在主人自己说第一句话时变成 <see cref="ThreadListing.Listed"/>。
    /// </summary>
    public ThreadListing Listing { get; set; }

    /// <summary>
    /// 线程元数据（JSON）
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// 不再写入也不再读取的旧列：早期版本在这里存过一份对话正文的序列化副本。
    /// 历史一律从消息表加载；删除线程时清空这一列，免得软删除的行带着正文留下来。
    /// 保留列本身只是为了不让升级产生一次结构迁移。
    /// </summary>
    public string? SerializedData { get; set; }

    /// <summary>
    /// 最后活动时间：任何一条消息写进线程时更新（主人的消息与 AI 的回答都算）。只作展示与导出，不决定列表顺序。
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// 主人最近一次在这条线程里发消息的时间（UTC）：一条用户消息写进线程时更新，只往后走；null = 还没有过。
    /// 应用替主人开始的一轮（自动化、渠道消息、从别的对话交过来的任务）写下的也是用户消息，同样算。
    /// AI 的回答、一轮结束、改标题、标记已读都不碰它。
    /// </summary>
    /// <remarks>
    /// 线程列表的层内顺序按它排（没有时用 创建时间顶上），所以列表里只有主人自己的动作挪动一行。
    /// 排队消息在主人排下它的那一刻就记在这里，交给 agent 时按排下的时间写，不再挪动一次。
    /// </remarks>
    public DateTime? LastUserMessageTime { get; set; }

    /// <summary>
    /// 归档时间；null = 未归档。归档的线程离开「最近对话」，内容与可续聊性不变；
    /// 用户在里面再发一条消息即取消归档。
    /// </summary>
    public DateTime? ArchivedTime { get; set; }

    /// <summary>
    /// 服务端的一轮最近一次把回答写进这条线程的时间（UTC）：在服务端跑的运行（外部 agent）写下回答时更新，只往后走。
    /// null = 还没有过这样的回答。与 <see cref="ReadTime"/> 一起回答「有没有主人还没看过的回答」。
    /// </summary>
    /// <remarks>
    /// 时间取自写下回答的那一刻，早于客户端从流里看到这一轮结束：看着这条线程的页面在流结束时标记已读，记下的
    /// <see cref="ReadTime"/> 一定不早于它，不会把看着结束的一轮算成未读。
    /// </remarks>
    public DateTime? ReplyTime { get; set; }

    /// <summary>
    /// 主人最近一次看过这条线程的时间（UTC）：客户端打开它、或它在屏幕上时一轮结束，经已读接口写下。null = 从未标记过。
    /// </summary>
    public DateTime? ReadTime { get; set; }

    /// <summary>
    /// 消息历史
    /// </summary>
    public virtual ICollection<AgentThreadMessage> Messages { get; set; } = new List<AgentThreadMessage>();
}
