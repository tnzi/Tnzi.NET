namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 会话线程实体
/// </summary>
public class AgentThread : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public Guid AgentId { get; set; }

    /// <summary>
    /// Agent
    /// </summary>
    public virtual Agent? Agent { get; set; }

    /// <summary>
    /// 线程标题
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 线程元数据（JSON）
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// ConversationContext 序列化数据（JSON）
    /// 用于完整恢复对话上下文的状态
    /// </summary>
    public string? SerializedData { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// 消息历史
    /// </summary>
    public virtual ICollection<AgentThreadMessage> Messages { get; set; } = new List<AgentThreadMessage>();
}
