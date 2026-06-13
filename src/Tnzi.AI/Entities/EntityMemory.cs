namespace Tnzi.AI.Entities;

/// <summary>
/// 实体记忆 — 存储跨会话识别的命名实体（人物、组织、地点、概念）
/// </summary>
/// <remarks>
/// 删除语义与 <see cref="MemoryEntry"/> 对齐为<b>硬删除</b>（<see cref="CreationAuditedEntity{TKey}"/> +
/// <see cref="IMultiTenant"/>，无软删）。理由：记忆存储层（DatabaseMemoryStore / DatabaseEntityMemoryStore）
/// 一律走 ExecuteDelete 硬删；"遗忘"应为真实删除（隐私 / 右遗忘 + 回收空间），软删的 IsDeleted 列在此为死列，
/// 且唯一索引未过滤 IsDeleted 会让软删行阻塞同名实体重建。会话隔离沿用 UserId + AgentId 列（不引入投机的
/// SessionId 列）；去重沿用 (EntityName, UserId, AgentId) 唯一 upsert（与 MemoryEntry 的语义合并去重不同——
/// 命名实体用精确键 upsert 是正确的，故意保留差异）。
/// </remarks>
public class EntityMemory : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 租户 ID
    /// </summary>
    public Guid? TenantId { get; set; }

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
