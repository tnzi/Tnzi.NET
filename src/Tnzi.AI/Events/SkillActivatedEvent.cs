namespace Tnzi.AI.Events;

/// <summary>
/// Skill activated event - published when a skill is activated via API or on-demand tool.
/// Applications can handle this event for usage tracking and analytics.
/// </summary>
public class SkillActivatedEvent : EventBase
{
    /// <summary>Skill slug</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Skill scope (System, Tenant, User)</summary>
    public SkillScope Scope { get; set; }

    /// <summary>Skill source (FileSystem, Database)</summary>
    public SkillSource Source { get; set; }

    /// <summary>Activation timestamp</summary>
    public DateTime ActivatedAt { get; set; }

    /// <summary>User who activated the skill (null if anonymous)</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Tenant ID of the resolved skill row (non-null for Tenant-scoped DB skills).
    /// Used by the event handler to narrow the activation-count increment to the exact row
    /// and prevent cross-tenant stat bleed.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT named <c>TenantId</c>: <see cref="EventBase.TenantId"/> is the ambient
    /// publishing tenant captured by the bus (used to restore tenant context in background handlers),
    /// which is a different value with a different purpose. Shadowing it would make the value read
    /// depend on the static type of the reference.
    /// </remarks>
    public Guid? SkillTenantId { get; set; }

    /// <summary>
    /// Owner user ID of the resolved skill row (non-null for User-scoped DB skills).
    /// Used together with <see cref="Slug"/> and <see cref="Scope"/> to uniquely identify
    /// the row to increment.
    /// </summary>
    public Guid? OwnerUserId { get; set; }
}
