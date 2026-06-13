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

    /// <summary>Tool groups（JSON 值转换列，与 Agent.Domains/Roles 同模式）</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>Excluded tool groups（JSON 值转换列）</summary>
    public List<string>? ExcludedToolGroups { get; set; }

    /// <summary>Maximum turns</summary>
    public int MaxTurns { get; set; } = 50;

    /// <summary>System instructions</summary>
    public string? Instructions { get; set; }

    /// <summary>Default model</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Default approval mode</summary>
    public ToolApprovalMode? DefaultApprovalMode { get; set; }

    /// <summary>Capability tags（JSON 值转换列）</summary>
    public List<string>? CapabilityTags { get; set; }

    /// <summary>Whether this type is enabled</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Tenant ID</summary>
    public Guid? TenantId { get; set; }
}
