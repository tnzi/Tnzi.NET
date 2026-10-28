namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 线程消息实体 - 存储对话历史
/// </summary>
public class AgentThreadMessage : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属线程 ID
    /// </summary>
    public Guid ThreadId { get; set; }

    /// <summary>
    /// 消息角色: system, user, assistant, tool
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 工具调用信息（JSON，可选）
    /// </summary>
    public string? ToolCalls { get; set; }

    /// <summary>
    /// Token 使用量（JSON，可选）
    /// </summary>
    public string? Usage { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 回复没有正常完成时的结局（<see cref="ThreadMessageStatuses"/>：<c>stopped</c> / <c>failed</c> / <c>usage_limit</c>）；正常完成为 null。
    /// </summary>
    /// <remarks>
    /// 内建引擎只存正常完成的回复，从不写它。外部 CLI 运行被用户停下、或中途失败时，把已经写出的部分与已经发生的工具调用
    /// 照样存下来（那是用户当时看到的、也是 CLI 会话里已经有的），并用它标明这是半截回复。
    /// </remarks>
    public string? Status { get; set; }

    /// <summary>
    /// <see cref="Status"/> 为 <c>usage_limit</c> 时，外部 CLI 账号用量上限的重置时间（UTC）；CLI 没报出来时为 null。
    /// </summary>
    public DateTime? UsageLimitResetsAt { get; set; }

    /// <summary>
    /// 用户消息：客户端随这一轮发来的每轮元数据（<c>ChatRequestDto.Metadata</c>，JSON 对象），由消费方定义键与值；
    /// 没带时为 null。随消息存下是为了重新生成时原样再带一次、重新加载时客户端还看得到这一轮指向了什么。
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// JSON array of component IDs attached to this message (for session restore).
    /// The framework neither reads nor writes it; an application that emits <c>StreamEvent.ComponentRef</c> persists the IDs here.
    /// </summary>
    public string? ComponentRefs { get; set; }

    /// <summary>
    /// 反馈评价: true=👍, false=👎, null=未评价
    /// </summary>
    public bool? FeedbackRating { get; set; }

    /// <summary>
    /// 差评原因标签（JSON 数组，多选），如 ["incorrect","too_long"]
    /// </summary>
    public string? FeedbackTags { get; set; }

    /// <summary>
    /// 反馈文本
    /// </summary>
    public string? FeedbackComment { get; set; }

    /// <summary>
    /// 反馈提交时间
    /// </summary>
    public DateTime? FeedbackTime { get; set; }

    /// <summary>
    /// 导航属性
    /// </summary>
    public virtual AgentThread? Thread { get; set; }
}
