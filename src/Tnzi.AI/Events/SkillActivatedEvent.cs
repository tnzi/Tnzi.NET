namespace Tnzi.AI.Events;

/// <summary>
/// Skill activated event — published when a skill is activated via API or on-demand tool.
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
}
