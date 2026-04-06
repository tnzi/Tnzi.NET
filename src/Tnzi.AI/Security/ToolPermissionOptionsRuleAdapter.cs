namespace Tnzi.AI.Security;

using Tnzi.AI.Options;

/// <summary>
/// 将配置选项转换为运行时权限规则。
/// </summary>
public static class ToolPermissionOptionsRuleAdapter
{
    public static IReadOnlyList<ToolPermissionRule> ToRules(ToolPermissionOptions? options)
    {
        if (options == null || !options.Enabled)
        {
            return [];
        }

        var rules = new List<ToolPermissionRule>();
        AddRules(rules, options.SystemRules, ToolPermissionScope.System);
        AddRules(rules, options.ProjectRules, ToolPermissionScope.Project);
        AddRules(rules, options.UserRules, ToolPermissionScope.User);
        AddRules(rules, options.SessionRules, ToolPermissionScope.Session);
        return rules;
    }

    private static void AddRules(
        ICollection<ToolPermissionRule> target,
        IEnumerable<ToolPermissionRuleOptions>? source,
        ToolPermissionScope scope)
    {
        if (source == null)
        {
            return;
        }

        foreach (var rule in source)
        {
            if (rule == null)
            {
                continue;
            }

            target.Add(new ToolPermissionRule
            {
                ToolPattern = NormalizeToolPattern(rule.ToolPattern),
                ToolGroup = NormalizeOptional(rule.ToolGroup),
                CommandPrefix = NormalizeOptional(rule.CommandPrefix),
                ServerName = NormalizeOptional(rule.ServerName),
                PathPrefix = NormalizeOptional(rule.PathPrefix),
                Behavior = rule.Behavior,
                Scope = scope,
                Priority = rule.Priority,
                IsDestructiveOnly = rule.IsDestructiveOnly,
                IsSubAgentOnly = rule.IsSubAgentOnly,
                SubAgentName = NormalizeOptional(rule.SubAgentName),
                IsWorkflowOnly = rule.IsWorkflowOnly,
                WorkflowNodeName = NormalizeOptional(rule.WorkflowNodeName),
                Reason = NormalizeOptional(rule.Reason)
            });
        }
    }

    private static string NormalizeToolPattern(string? value)
        => string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
