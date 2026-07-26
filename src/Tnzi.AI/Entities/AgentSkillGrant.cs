namespace Tnzi.AI.Entities;

/// <summary>
/// Agent ↔ 技能授权 junction 实体。把一个 Agent 与一个技能（按 slug）关联起来，
/// 取代 <see cref="Agent"/> 原先的 SkillSlugs JSON 列。FK 仅指向同程序集的 <see cref="Agent"/>。
/// Agent-to-skill grant junction - associates an Agent with a skill (by slug),
/// replacing <see cref="Agent"/>'s former SkillSlugs JSON column. FK targets the same-assembly
/// <see cref="Agent"/> only; the skill itself is referenced by value.
/// </summary>
public class AgentSkillGrant : MultiTenantAuditedEntity<Guid>, IGrantEnableState
{
    /// <summary>被授权的 Agent。The Agent this grant belongs to.</summary>
    public Guid AgentId { get; set; }

    /// <summary>导航到所属 Agent。Navigation to the owning Agent.</summary>
    public virtual Agent? Agent { get; set; }

    /// <summary>技能 slug（权威来源）。Skill slug (the source of truth).</summary>
    public string SkillSlug { get; set; } = string.Empty;

    /// <summary>是否启用本条授权。Whether this grant is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>优先级（越大越优先）。Priority (higher wins).</summary>
    public int Priority { get; set; }
}
