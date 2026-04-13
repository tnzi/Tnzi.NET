namespace Tnzi.AI.Dtos;

/// <summary>
/// 权限规则列表响应
/// </summary>
public class PermissionRulesDto
{
    /// <summary>评估器是否包含任何规则</summary>
    public bool HasRules { get; set; }

    /// <summary>当前 Session 级别规则</summary>
    public List<PermissionRuleItemDto> SessionRules { get; set; } = [];
}

/// <summary>
/// 权限规则条目
/// </summary>
public class PermissionRuleItemDto
{
    public string ToolPattern { get; set; } = "*";
    public string? ToolGroup { get; set; }
    public string? CommandPrefix { get; set; }
    public string? ServerName { get; set; }
    public string? PathPrefix { get; set; }
    public bool IsSubAgentOnly { get; set; }
    public string? SubAgentName { get; set; }
    public bool IsWorkflowOnly { get; set; }
    public string? WorkflowNodeName { get; set; }
    public PermissionBehavior Behavior { get; set; }
    public ToolPermissionScope Scope { get; set; }
    public int Priority { get; set; }
    public bool IsDestructiveOnly { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// 权限评估测试请求
/// </summary>
public class PermissionEvaluateRequestDto
{
    /// <summary>工具名称</summary>
    [Required]
    public string? ToolName { get; set; }

    /// <summary>工具组</summary>
    public string? ToolGroup { get; set; }

    /// <summary>工作目录</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>候选路径</summary>
    public List<string>? CandidatePaths { get; set; }

    /// <summary>MCP server 名称</summary>
    public string? ServerName { get; set; }

    /// <summary>是否子 Agent</summary>
    public bool IsSubAgent { get; set; }

    /// <summary>子 Agent 名称</summary>
    public string? SubAgentName { get; set; }

    /// <summary>是否 workflow 运行</summary>
    public bool IsWorkflowRun { get; set; }

    /// <summary>workflow 定义 ID</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>workflow 执行 ID</summary>
    public string? WorkflowExecutionId { get; set; }

    /// <summary>workflow 节点名称</summary>
    public string? WorkflowNodeName { get; set; }

    /// <summary>Shell 命令</summary>
    public string? ShellCommand { get; set; }

    /// <summary>是否破坏性工具</summary>
    public bool IsDestructive { get; set; }

    /// <summary>工具参数</summary>
    public Dictionary<string, object?>? Arguments { get; set; }
}

/// <summary>
/// 权限评估结果
/// </summary>
public class PermissionEvaluateResultDto
{
    public string ToolName { get; set; } = string.Empty;
    public PermissionBehavior Behavior { get; set; }
    public string? Reason { get; set; }
    public ToolPermissionScope? Scope { get; set; }
    public string? MatchedRulePattern { get; set; }
    public string? MatchedToolGroup { get; set; }
    public string? MatchedServerName { get; set; }
    public string? MatchedPathPrefix { get; set; }
    public string? MatchedSubAgentName { get; set; }
    public string? MatchedWorkflowNodeName { get; set; }
}

/// <summary>
/// 持久化权限规则响应
/// </summary>
public class PersistedPermissionRuleDto
{
    public Guid Id { get; set; }
    public string? ToolPattern { get; set; }
    public string? ToolGroup { get; set; }
    public string? CommandPrefix { get; set; }
    public string? ServerName { get; set; }
    public string? PathPrefix { get; set; }
    public PermissionBehavior Behavior { get; set; }
    public ToolPermissionScope Scope { get; set; }
    public int Priority { get; set; }
    public bool IsDestructiveOnly { get; set; }
    public bool IsSubAgentOnly { get; set; }
    public string? Reason { get; set; }
    public Guid? UserId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 创建持久化权限规则请求
/// </summary>
public class CreatePersistedPermissionRuleDto
{
    /// <summary>工具名称模式（支持通配符 *）</summary>
    public string? ToolPattern { get; set; }

    /// <summary>工具组</summary>
    public string? ToolGroup { get; set; }

    /// <summary>命令前缀</summary>
    public string? CommandPrefix { get; set; }

    /// <summary>MCP server 名称</summary>
    public string? ServerName { get; set; }

    /// <summary>路径前缀</summary>
    public string? PathPrefix { get; set; }

    /// <summary>权限行为</summary>
    [Required]
    public PermissionBehavior Behavior { get; set; }

    /// <summary>规则来源范围</summary>
    [Required]
    public ToolPermissionScope Scope { get; set; }

    /// <summary>优先级</summary>
    public int Priority { get; set; }

    /// <summary>仅匹配破坏性工具</summary>
    public bool IsDestructiveOnly { get; set; }

    /// <summary>仅匹配子 Agent 调用</summary>
    public bool IsSubAgentOnly { get; set; }

    /// <summary>原因说明</summary>
    public string? Reason { get; set; }

    /// <summary>关联用户 ID</summary>
    public Guid? UserId { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
