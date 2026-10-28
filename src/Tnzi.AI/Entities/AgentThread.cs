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
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// 归档时间；null = 未归档。归档的线程离开「最近对话」，内容与可续聊性不变；
    /// 用户在里面再发一条消息即取消归档。
    /// </summary>
    public DateTime? ArchivedTime { get; set; }

    /// <summary>
    /// 消息历史
    /// </summary>
    public virtual ICollection<AgentThreadMessage> Messages { get; set; } = new List<AgentThreadMessage>();
}
