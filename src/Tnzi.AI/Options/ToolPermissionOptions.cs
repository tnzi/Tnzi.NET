namespace Tnzi.AI.Options;

/// <summary>
/// 工具权限规则配置
/// </summary>
public class ToolPermissionOptions
{
    /// <summary>
    /// 是否启用规则引擎
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 系统级规则
    /// </summary>
    public List<ToolPermissionRuleOptions> SystemRules { get; set; } = [];

    /// <summary>
    /// 项目级规则
    /// </summary>
    public List<ToolPermissionRuleOptions> ProjectRules { get; set; } = [];

    /// <summary>
    /// 用户级规则
    /// </summary>
    public List<ToolPermissionRuleOptions> UserRules { get; set; } = [];

    /// <summary>
    /// 会话级规则
    /// </summary>
    public List<ToolPermissionRuleOptions> SessionRules { get; set; } = [];
}

/// <summary>
/// 单条工具权限规则配置
/// </summary>
public class ToolPermissionRuleOptions
{
    /// <summary>工具名称模式，支持 * 前缀通配</summary>
    public string ToolPattern { get; set; } = "*";

    /// <summary>工具组</summary>
    public string? ToolGroup { get; set; }

    /// <summary>命令前缀</summary>
    public string? CommandPrefix { get; set; }

    /// <summary>MCP server 名称</summary>
    public string? ServerName { get; set; }

    /// <summary>路径前缀</summary>
    public string? PathPrefix { get; set; }

    /// <summary>权限行为</summary>
    public PermissionBehavior Behavior { get; set; } = PermissionBehavior.Allow;

    /// <summary>优先级</summary>
    public int Priority { get; set; }

    /// <summary>仅匹配破坏性调用</summary>
    public bool IsDestructiveOnly { get; set; }

    /// <summary>仅匹配子 Agent</summary>
    public bool IsSubAgentOnly { get; set; }

    /// <summary>子 Agent 名称</summary>
    public string? SubAgentName { get; set; }

    /// <summary>仅匹配 workflow 调用</summary>
    public bool IsWorkflowOnly { get; set; }

    /// <summary>workflow 节点名称</summary>
    public string? WorkflowNodeName { get; set; }

    /// <summary>原因说明</summary>
    public string? Reason { get; set; }
}
