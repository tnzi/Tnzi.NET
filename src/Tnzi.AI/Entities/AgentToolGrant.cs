namespace Tnzi.AI.Entities;

/// <summary>
/// Agent ↔ 工具授权 junction 实体。把一个 Agent 与一个工具组或单个工具关联起来，
/// 取代 <see cref="Agent"/> 原先的 ToolGroups JSON 列。FK 仅指向同程序集的 <see cref="Agent"/>；
/// 工具按值（字符串）引用——工具组/工具没有数据库实体，名称即权威来源。
/// Agent-to-tool grant junction — associates an Agent with a tool group or a single tool,
/// replacing <see cref="Agent"/>'s former ToolGroups JSON column. The tool is referenced by value
/// (string), since tool groups/tools have no DB entity (the name is the source of truth).
/// </summary>
public class AgentToolGrant : MultiTenantAuditedEntity<Guid>, IGrantEnableState
{
    /// <summary>被授权的 Agent。The Agent this grant belongs to.</summary>
    public Guid AgentId { get; set; }

    /// <summary>导航到所属 Agent。Navigation to the owning Agent.</summary>
    public virtual Agent? Agent { get; set; }

    /// <summary>授权粒度（Group = 工具组，Tool = 单个工具）。Grant granularity (Group or Tool).</summary>
    public GrantType GrantType { get; set; }

    /// <summary>
    /// 工具键：<see cref="GrantType.Group"/> 时为工具组名，<see cref="GrantType.Tool"/> 时为工具名。
    /// Tool key — the tool group name when GrantType=Group, the tool name when GrantType=Tool.
    /// </summary>
    public string ToolKey { get; set; } = string.Empty;

    /// <summary>是否启用本条授权。Whether this grant is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>优先级（越大越优先）。Priority (higher wins).</summary>
    public int Priority { get; set; }
}
