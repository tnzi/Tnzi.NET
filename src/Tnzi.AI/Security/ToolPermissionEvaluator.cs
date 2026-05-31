namespace Tnzi.AI.Security;

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
