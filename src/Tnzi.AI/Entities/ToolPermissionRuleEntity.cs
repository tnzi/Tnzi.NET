namespace Tnzi.AI.Entities;

/// <summary>
/// 工具权限规则持久化实体 — 存储管理员配置的 allow/deny/ask 规则
/// </summary>
public class ToolPermissionRuleEntity : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>工具名称模式（支持通配符 *）</summary>
    public string? ToolPattern { get; set; }

    /// <summary>工具组</summary>
    public string? ToolGroup { get; set; }

    /// <summary>命令前缀（用于 shell 类工具）</summary>
    public string? CommandPrefix { get; set; }

    /// <summary>MCP server 名称</summary>
    public string? ServerName { get; set; }

    /// <summary>路径前缀</summary>
    public string? PathPrefix { get; set; }

    /// <summary>权限行为 (0=Allow, 1=Ask, 2=Deny)</summary>
    public int Behavior { get; set; }

    /// <summary>规则来源范围 (0=System, 1=Project, 2=User, 3=Session)</summary>
    public int Scope { get; set; }

    /// <summary>规则优先级，值越大优先级越高</summary>
    public int Priority { get; set; }

    /// <summary>仅匹配破坏性工具</summary>
    public bool IsDestructiveOnly { get; set; }

    /// <summary>仅匹配子 Agent 调用</summary>
    public bool IsSubAgentOnly { get; set; }

    /// <summary>原因说明</summary>
    public string? Reason { get; set; }

    /// <summary>关联用户 ID（User 级规则）</summary>
    public Guid? UserId { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>租户 ID</summary>
    public Guid? TenantId { get; set; }
}
