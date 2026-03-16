namespace Tnzi.AI.Entities;

/// <summary>
/// 实体记忆 — 存储跨会话识别的命名实体（人物、组织、地点、概念）
/// </summary>
public class EntityMemory : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 实体名称
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// 实体类型（person, organization, location, concept）
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 实体属性（JSON 格式）
    /// </summary>
    public string Properties { get; set; } = "{}";

    /// <summary>
    /// 最后一次提及时间
    /// </summary>
    public DateTime LastMentioned { get; set; }

    /// <summary>
    /// 提及次数
    /// </summary>
    public int MentionCount { get; set; }

    /// <summary>
    /// 关联用户 ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 关联 Agent ID
    /// </summary>
    public Guid? AgentId { get; set; }
}
