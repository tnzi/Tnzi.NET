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

    /// <summary>原因说明</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// 工具权限评估器
/// </summary>
public class ToolPermissionEvaluator : IToolPermissionEvaluator
{
    private readonly List<ToolPermissionRule> _rules;
    private readonly object _sessionLock = new();
    private readonly List<ToolPermissionRule> _sessionRules = [];

    public ToolPermissionEvaluator(IEnumerable<ToolPermissionRule> rules)
    {
        _rules = Check.NotNull(rules).ToList();
    }

    /// <inheritdoc />
    public bool HasRules => _rules.Count > 0 || GetSessionRules().Count > 0;

    /// <summary>
    /// 评估工具是否被允许。
    /// 规则冲突时按 Priority、Scope、Behavior 依次决胜。
    /// 破坏性工具（IsDestructive）在没有任何命中规则时默认拒绝。
    /// </summary>
    public ToolPermissionDecision Evaluate(string toolName, bool isDestructive = false, string? toolGroup = null)
    {
        return Evaluate(new ToolPermissionContext
        {
            ToolName = toolName,
            ToolGroup = toolGroup,
            IsDestructive = isDestructive
        });
    }

    /// <inheritdoc />
    public void AddSessionRule(ToolPermissionRule rule)
    {
        Check.NotNull(rule);
        rule.Scope = ToolPermissionScope.Session;

        lock (_sessionLock)
        {
            _sessionRules.Add(rule);
        }
    }

    /// <inheritdoc />
    public void RemoveSessionRule(string toolPattern)
    {
        Check.NotNullOrWhiteSpace(toolPattern);

        lock (_sessionLock)
        {
            _sessionRules.RemoveAll(r =>
                string.Equals(r.ToolPattern, toolPattern, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPermissionRule> GetSessionRules()
    {
        lock (_sessionLock)
        {
            return _sessionRules.ToList();
        }
    }

    /// <inheritdoc />
    public ToolPermissionDecision Evaluate(ToolPermissionContext context, IEnumerable<ToolPermissionRule>? additionalRules = null)
    {
        Check.NotNull(context);
        Check.NotNullOrWhiteSpace(context.ToolName);

        var allRules = _rules.AsEnumerable();

        var sessionRules = GetSessionRules();
        if (sessionRules.Count > 0)
        {
            allRules = allRules.Concat(sessionRules);
        }

        var matchedRules = allRules
            .Concat(additionalRules ?? Enumerable.Empty<ToolPermissionRule>())
            .Where(rule => MatchesRule(context, rule))
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => GetScopeWeight(r.Scope))
            .ThenByDescending(r => GetBehaviorWeight(r.Behavior))
            .ToList();

        if (matchedRules.Count == 0)
        {
            if (context.IsDestructive)
            {
                return new ToolPermissionDecision(context.ToolName, PermissionBehavior.Deny, "Destructive tool requires explicit allow");
            }

            return new ToolPermissionDecision(context.ToolName, PermissionBehavior.Allow);
        }

        var winner = matchedRules[0];
        return new ToolPermissionDecision(
            context.ToolName,
            winner.Behavior,
            GetReason(winner))
        {
            Scope = winner.Scope,
            MatchedRulePattern = winner.ToolPattern,
            MatchedToolGroup = winner.ToolGroup,
            MatchedServerName = winner.ServerName,
            MatchedPathPrefix = winner.PathPrefix,
            MatchedSubAgentName = winner.SubAgentName,
            MatchedWorkflowNodeName = winner.WorkflowNodeName
        };
    }

    private static bool MatchesRule(ToolPermissionContext context, ToolPermissionRule rule)
    {
        if (rule.IsDestructiveOnly && !context.IsDestructive)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ToolGroup)
            && !string.Equals(rule.ToolGroup, context.ToolGroup, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!MatchesCommandPrefix(context, rule.CommandPrefix))
        {
            return false;
        }

        if (!MatchesServerName(context, rule.ServerName))
        {
            return false;
        }

        if (!MatchesPathPrefix(context, rule.PathPrefix))
        {
            return false;
        }

        if (!MatchesSubAgentContext(context, rule))
        {
            return false;
        }

        if (!MatchesWorkflowContext(context, rule))
        {
            return false;
        }

        return MatchesPattern(context.ToolName, rule.ToolPattern);
    }

    private static bool MatchesCommandPrefix(ToolPermissionContext context, string? commandPrefix)
    {
        if (string.IsNullOrWhiteSpace(commandPrefix))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(context.ShellCommand)
            && context.ShellCommand.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var segment in context.ShellSegments)
        {
            if (segment.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPattern(string toolName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        if (pattern == "*") return true;

        if (pattern.EndsWith('*'))
        {
            return toolName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(toolName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesServerName(ToolPermissionContext context, string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return true;
        }

        return string.Equals(context.ServerName, serverName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPathPrefix(ToolPermissionContext context, string? pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return true;
        }

        var normalizedPrefix = NormalizePath(pathPrefix);
        if (normalizedPrefix == null)
        {
            return false;
        }

        foreach (var candidate in context.CandidatePaths)
        {
            var normalizedCandidate = NormalizePath(candidate);
            if (MatchesPathBoundary(normalizedCandidate, normalizedPrefix))
            {
                return true;
            }
        }

        var normalizedWorkingDirectory = NormalizePath(context.WorkingDirectory);
        return MatchesPathBoundary(normalizedWorkingDirectory, normalizedPrefix);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path
            .Trim()
            .Replace('/', '\\')
            .TrimEnd('\\');
    }

    private static bool MatchesPathBoundary(string? candidatePath, string normalizedPrefix)
    {
        if (candidatePath == null)
        {
            return false;
        }

        if (string.Equals(candidatePath, normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidatePath.Length > normalizedPrefix.Length
            && candidatePath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
            && candidatePath[normalizedPrefix.Length] == '\\';
    }

    private static bool MatchesSubAgentContext(ToolPermissionContext context, ToolPermissionRule rule)
    {
        if (rule.IsSubAgentOnly && !context.IsSubAgent)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.SubAgentName)
            && !string.Equals(rule.SubAgentName, context.SubAgentName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesWorkflowContext(ToolPermissionContext context, ToolPermissionRule rule)
    {
        if (rule.IsWorkflowOnly && !context.IsWorkflowRun)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.WorkflowNodeName)
            && !string.Equals(rule.WorkflowNodeName, context.WorkflowNodeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetScopeWeight(ToolPermissionScope scope)
        => scope switch
        {
            ToolPermissionScope.Session => 4,
            ToolPermissionScope.User => 3,
            ToolPermissionScope.Project => 2,
            _ => 1
        };

    private static int GetBehaviorWeight(PermissionBehavior behavior)
        => behavior switch
        {
            PermissionBehavior.Deny => 3,
            PermissionBehavior.Ask => 2,
            _ => 1
        };

    private static string? GetReason(ToolPermissionRule rule)
        => rule.Behavior switch
        {
            PermissionBehavior.Deny => rule.Reason ?? "Denied by rule",
            PermissionBehavior.Ask => rule.Reason ?? "Approval required by rule",
            _ => rule.Reason
        };
}
