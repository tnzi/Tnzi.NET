namespace Tnzi.AI.Security;

/// <summary>
/// 工具权限决策行为
/// </summary>
public enum PermissionBehavior
{
    /// <summary>允许执行</summary>
    Allow,

    /// <summary>需要进入审批流程</summary>
    Ask,

    /// <summary>拒绝执行</summary>
    Deny
}

/// <summary>
/// 权限规则来源范围
/// </summary>
public enum ToolPermissionScope
{
    System = 0,
    Project = 1,
    User = 2,
    Session = 3
}

/// <summary>
/// 工具权限评估上下文
/// </summary>
public sealed class ToolPermissionContext
{
    /// <summary>工具名称</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>工具组</summary>
    public string? ToolGroup { get; set; }

    /// <summary>工具参数</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; set; } = new Dictionary<string, object?>();

    /// <summary>工作目录</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>与本次调用相关的文件路径/目录路径</summary>
    public IReadOnlyList<string> CandidatePaths { get; set; } = Array.Empty<string>();

    /// <summary>MCP server 名称</summary>
    public string? ServerName { get; set; }

    /// <summary>当前调用是否发生在子 Agent 内</summary>
    public bool IsSubAgent { get; set; }

    /// <summary>子 Agent 名称</summary>
    public string? SubAgentName { get; set; }

    /// <summary>当前调用是否发生在 workflow 运行内</summary>
    public bool IsWorkflowRun { get; set; }

    /// <summary>当前 workflow 定义 ID</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>当前 workflow 执行实例 ID</summary>
    public string? WorkflowExecutionId { get; set; }

    /// <summary>当前 workflow 节点名称</summary>
    public string? WorkflowNodeName { get; set; }

    /// <summary>原始 shell 命令</summary>
    public string? ShellCommand { get; set; }

    /// <summary>解析后的 shell 命令片段</summary>
    public IReadOnlyList<string> ShellSegments { get; set; } = Array.Empty<string>();

    /// <summary>是否为破坏性工具</summary>
    public bool IsDestructive { get; set; }

    /// <summary>
    /// 发起本次调用的用户；User 级规则靠它绑定到人。取不到调用者时为 null，
    /// 此时任何 <see cref="ToolPermissionRule.UserId"/> 非空的规则都不匹配 ——
    /// 不能因为不知道是谁就假定「就是那个人」。
    /// </summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// 工具权限评估器接口
/// </summary>
public interface IToolPermissionEvaluator
{
    /// <summary>
    /// 当前评估器是否包含持久或全局规则
    /// </summary>
    bool HasRules { get; }

    /// <summary>
    /// 评估工具权限
    /// </summary>
    ToolPermissionDecision Evaluate(ToolPermissionContext context, IEnumerable<ToolPermissionRule>? additionalRules = null);

    /// <summary>
    /// 添加一条 Session 级别的动态规则
    /// </summary>
    void AddSessionRule(ToolPermissionRule rule)
    {
        // Default no-op for backward compatibility
    }

    /// <summary>
    /// 移除匹配指定 ToolPattern 的所有 Session 级别规则
    /// </summary>
    void RemoveSessionRule(string toolPattern)
    {
        // Default no-op for backward compatibility
    }

    /// <summary>
    /// 获取当前所有 Session 级别规则
    /// </summary>
    IReadOnlyList<ToolPermissionRule> GetSessionRules() => [];

    /// <summary>
    /// Refresh cached rules from external sources (e.g. database). No-op by default.
    /// </summary>
    Task RefreshRulesAsync() => Task.CompletedTask;

    /// <summary>
    /// 用调用方已经读好的规则集替换缓存。
    /// </summary>
    /// <remarks>
    /// 写入侧必须走这一条：<see cref="RefreshRulesAsync()"/> 的实现会另开作用域、另开 DbContext 重查，
    /// 而宿主开着 <c>EnableGlobalUnitOfWork</c> 时本次 CRUD 的写入还在变更跟踪器里、
    /// 即便 flush 也只是同一条连接上的未提交状态 —— 另一条连接看不见。
    /// 结果就是刚建的规则在下一次 CRUD 或重启前完全不生效，而接口返回的是成功。
    /// 默认退回按需重查，供不缓存的实现使用。
    /// </remarks>
    /// <param name="rules">调用方在自己的作用域内读到的完整规则集</param>
    Task RefreshRulesAsync(IReadOnlyList<ToolPermissionRule> rules) => RefreshRulesAsync();
}

/// <summary>
/// 工具权限决策 -- 表示对特定工具的权限判断结果
/// </summary>
public record ToolPermissionDecision(
    string ToolName,
    PermissionBehavior Behavior,
    string? Reason = null)
{
    /// <summary>命中的规则来源范围</summary>
    public ToolPermissionScope? Scope { get; init; }

    /// <summary>命中的工具名称模式</summary>
    public string? MatchedRulePattern { get; init; }

    /// <summary>命中的工具组条件</summary>
    public string? MatchedToolGroup { get; init; }

    /// <summary>命中的 MCP server 条件</summary>
    public string? MatchedServerName { get; init; }

    /// <summary>命中的路径前缀条件</summary>
    public string? MatchedPathPrefix { get; init; }

    /// <summary>命中的子 Agent 名称条件</summary>
    public string? MatchedSubAgentName { get; init; }

    /// <summary>命中的 workflow 节点名称条件</summary>
    public string? MatchedWorkflowNodeName { get; init; }

    /// <summary>是否需要进入审批处理器</summary>
    public bool RequiresApprovalHandler => Behavior == PermissionBehavior.Ask;
}

/// <summary>
/// 工具权限规则 -- 配置式权限声明
/// </summary>
public class ToolPermissionRule
{
    /// <summary>工具名称（支持通配符 * 匹配）</summary>
    public string ToolPattern { get; set; } = "*";

    /// <summary>工具组（为空表示不限制）</summary>
    public string? ToolGroup { get; set; }

    /// <summary>命令前缀（用于 shell 类工具）</summary>
    public string? CommandPrefix { get; set; }

    /// <summary>MCP server 名称（为空表示不限制）</summary>
    public string? ServerName { get; set; }

    /// <summary>路径前缀（文件或目录，大小写不敏感前缀匹配）</summary>
    public string? PathPrefix { get; set; }

    /// <summary>仅匹配子 Agent 调用</summary>
    public bool IsSubAgentOnly { get; set; }

    /// <summary>子 Agent 名称（为空表示不限制）</summary>
    public string? SubAgentName { get; set; }

    /// <summary>仅匹配 workflow 运行</summary>
    public bool IsWorkflowOnly { get; set; }

    /// <summary>workflow 节点名称（为空表示不限制）</summary>
    public string? WorkflowNodeName { get; set; }

    /// <summary>权限行为</summary>
    public PermissionBehavior Behavior { get; set; }

    /// <summary>规则来源范围</summary>
    public ToolPermissionScope Scope { get; set; } = ToolPermissionScope.System;

    /// <summary>规则优先级，值越大优先级越高</summary>
    public int Priority { get; set; }

    /// <summary>仅匹配破坏性工具</summary>
    public bool IsDestructiveOnly { get; set; }

    /// <summary>
    /// 绑定的用户（User 级规则）。为空表示规则不绑任何人、对所有调用者生效 ——
    /// <see cref="Scope"/> 本身只参与冲突决胜，不构成用户过滤。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>原因说明</summary>
    public string? Reason { get; set; }
}
