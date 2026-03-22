namespace Tnzi.AI.Dtos;

/// <summary>
/// Agent 验证结果
/// </summary>
public class AgentValidationResultDto
{
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>Agent name</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Whether all checks passed</summary>
    public bool IsValid { get; set; }

    /// <summary>Individual validation check results</summary>
    public List<ValidationCheckDto> Checks { get; set; } = [];
}

/// <summary>
/// 单项验证检查结果
/// </summary>
public class ValidationCheckDto
{
    /// <summary>Check name (e.g., "provider", "model", "tool_groups", "handoff_targets")</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this check passed</summary>
    public bool Passed { get; set; }

    /// <summary>Details (error message if failed, or descriptive info if passed)</summary>
    public string? Details { get; set; }
}

/// <summary>
/// Agent 健康摘要 DTO
/// </summary>
public class AgentHealthSummaryDto
{
    /// <summary>Total agents</summary>
    public int TotalAgents { get; set; }

    /// <summary>Agents that passed all checks</summary>
    public int HealthyAgents { get; set; }

    /// <summary>Agents with validation warnings or errors</summary>
    public int UnhealthyAgents { get; set; }

    /// <summary>Disabled agents (not validated)</summary>
    public int DisabledAgents { get; set; }

    /// <summary>Unhealthy agent details (only the failing ones)</summary>
    public List<AgentValidationResultDto> UnhealthyDetails { get; set; } = [];
}
