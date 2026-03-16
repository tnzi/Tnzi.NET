namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 版本快照实体 — 每次 Agent 更新时自动创建的配置快照
/// </summary>
public class AgentVersion : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 关联的 Agent ID
    /// </summary>
    public Guid AgentId { get; set; }

    /// <summary>
    /// 版本号（自增，从 1 开始）
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// 变更说明（可选）
    /// </summary>
    public string? ChangeNote { get; set; }

    /// <summary>
    /// 完整配置快照（JSON 序列化的 Agent 配置）
    /// </summary>
    public string ConfigSnapshot { get; set; } = string.Empty;

    /// <summary>
    /// 导航属性
    /// </summary>
    public virtual Agent? Agent { get; set; }
}
