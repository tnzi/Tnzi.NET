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

        if (!MatchesCommandPrefix(context, rule.CommandPrefix, rule.Behavior))
        {
            return false;
        }

        if (!MatchesServerName(context, rule.ServerName))
        {
            return false;
        }

        if (!MatchesPathPrefix(context, rule.PathPrefix, rule.Behavior))
        {
            return false;
        }

        if (!MatchesUser(context, rule))
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

    /// <summary>
    /// 命令前缀规则是否覆盖这次调用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 放行规则（Allow）要求<b>每一个</b>片段都以该前缀开头，且落在词边界上：只看「任一片段」时，
    /// <c>bash Allow CommandPrefix=git</c> 会放行 <c>git status; rm -rf data</c>（第一个片段是 git），
    /// 不看词边界时还会放行 <c>gitx</c>。片段来自 <see cref="IShellCommandAnalyzer"/>，含命令替换、
    /// <c>bash -c</c> 与包装命令里的命令；没有片段（分析器不可用）时放行规则不命中（失败关闭）。
    /// </para>
    /// <para>
    /// 拒绝（Deny）与询问（Ask）规则保持「任一片段命中即命中」：多命中只会多拦或多问一次人，
    /// Ask 若也改成「全部命中」，往调用里多塞一个范围外的片段就能绕过本该询问的规则。
    /// </para>
    /// </remarks>
    private static bool MatchesCommandPrefix(ToolPermissionContext context, string? commandPrefix, PermissionBehavior behavior)
    {
        if (string.IsNullOrWhiteSpace(commandPrefix))
        {
            return true;
        }

        if (behavior == PermissionBehavior.Allow)
        {
            var prefix = commandPrefix.Trim();
            return context.ShellSegments.Count > 0
                && context.ShellSegments.All(segment => StartsWithCommand(segment, prefix));
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

    /// <summary>片段以该命令前缀开头，且前缀之后是片段结尾或空白（<c>git</c> 不命中 <c>gitx</c>）。</summary>
    private static bool StartsWithCommand(string segment, string prefix)
    {
        var text = segment.TrimStart();
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Length == prefix.Length || char.IsWhiteSpace(text[prefix.Length]);
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

    /// <summary>
    /// 路径前缀规则是否覆盖这次调用。
    /// </summary>
    /// <remarks>
    /// 放行规则（Allow）要求这次调用涉及的<b>每一个</b>路径都在前缀之下：候选路径包含模型自己填的
    /// <c>working_directory</c> 与每个名字像路径的参数，只看「任一路径」时，<c>write_file Allow PathPrefix=/workspace</c>
    /// 会被 <c>{path: "/etc/x", working_directory: "/workspace"}</c> 满足。没有任何路径时放行规则不命中。
    /// 拒绝与询问规则保持「任一路径命中即命中」，理由同 <see cref="MatchesCommandPrefix"/>。
    /// </remarks>
    private static bool MatchesPathPrefix(ToolPermissionContext context, string? pathPrefix, PermissionBehavior behavior)
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

        if (behavior == PermissionBehavior.Allow)
        {
            IReadOnlyList<string> paths = context.CandidatePaths.Count > 0
                ? context.CandidatePaths
                : string.IsNullOrWhiteSpace(context.WorkingDirectory) ? [] : [context.WorkingDirectory];
            return paths.Count > 0
                && paths.All(path => MatchesPathBoundary(NormalizePath(path), normalizedPrefix));
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

    /// <summary>
    /// 用户绑定匹配。规则未绑用户（UserId 为空）时对所有调用者生效；绑了用户时必须与调用者一致 ——
    /// 调用者未知（context.UserId 为空）也不匹配，不能因为不知道是谁就假定「就是那个人」。
    /// </summary>
    private static bool MatchesUser(ToolPermissionContext context, ToolPermissionRule rule)
    {
        if (rule.UserId == null)
        {
            return true;
        }

        return context.UserId.HasValue && context.UserId.Value == rule.UserId.Value;
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
