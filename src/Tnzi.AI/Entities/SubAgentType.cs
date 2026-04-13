namespace Tnzi.AI.Entities;

/// <summary>
/// 子 Agent 类型定义实体 — 持久化 SubAgentRegistry 中的自定义类型
/// </summary>
public class SubAgentType : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>Type name (unique identifier, e.g. code-reviewer)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Tool groups (JSON array)</summary>
    public string? ToolGroupsJson { get; set; }

    /// <summary>Excluded tool groups (JSON array)</summary>
    public string? ExcludedToolGroupsJson { get; set; }

    /// <summary>Maximum turns</summary>
    public int MaxTurns { get; set; } = 50;

    /// <summary>System instructions</summary>
    public string? Instructions { get; set; }

    /// <summary>Default model</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Default approval mode (int mapping to ToolApprovalMode enum)</summary>
    public int? DefaultApprovalMode { get; set; }

    /// <summary>Capability tags (JSON array)</summary>
    public string? CapabilityTagsJson { get; set; }

    /// <summary>Whether this type is enabled</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Tenant ID</summary>
    public Guid? TenantId { get; set; }
}
