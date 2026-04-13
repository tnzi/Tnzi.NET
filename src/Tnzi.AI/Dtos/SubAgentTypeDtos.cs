namespace Tnzi.AI.Dtos;

/// <summary>
/// 子 Agent 类型输出 DTO
/// </summary>
public class SubAgentTypeDto
{
    /// <summary>ID</summary>
    public Guid Id { get; set; }

    /// <summary>Type name (unique identifier)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Tool groups</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>Excluded tool groups</summary>
    public List<string>? ExcludedToolGroups { get; set; }

    /// <summary>Maximum turns</summary>
    public int MaxTurns { get; set; }

    /// <summary>System instructions</summary>
    public string? Instructions { get; set; }

    /// <summary>Default model</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Default approval mode</summary>
    public ToolApprovalMode? DefaultApprovalMode { get; set; }

    /// <summary>Capability tags</summary>
    public List<string>? CapabilityTags { get; set; }

    /// <summary>Whether this type is enabled</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Creation time</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Last modification time</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 子 Agent 类型输入 DTO（创建/更新共用）
/// </summary>
public class SubAgentTypeInputDto
{
    /// <summary>Type name (unique identifier)</summary>
    [Required]
    public string Name { get; set; } = null!;

    /// <summary>Type description</summary>
    [Required]
    public string Description { get; set; } = null!;

    /// <summary>Tool groups</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>Excluded tool groups</summary>
    public List<string>? ExcludedToolGroups { get; set; }

    /// <summary>Maximum turns (default 50)</summary>
    public int MaxTurns { get; set; } = 50;

    /// <summary>System instructions</summary>
    public string? Instructions { get; set; }

    /// <summary>Default model</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Default approval mode</summary>
    public ToolApprovalMode? DefaultApprovalMode { get; set; }

    /// <summary>Capability tags</summary>
    public List<string>? CapabilityTags { get; set; }

    /// <summary>Whether this type is enabled (default true)</summary>
    public bool IsEnabled { get; set; } = true;
}
