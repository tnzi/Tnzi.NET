namespace Tnzi.AI.Security;

using Tnzi.AI.Options;

/// <summary>
/// 将旧的 ToolApprovalOptions 适配为 ToolPermissionRule。
/// 这是兼容层，避免在接入权限引擎的第一阶段破坏既有配置行为。
/// </summary>
public static class ToolApprovalOptionsRuleAdapter
{
    public static IReadOnlyList<ToolPermissionRule> ToRules(ToolApprovalOptions options)
    {
        if (options == null || !options.Enabled)
        {
            return [];
        }

        if (options.Mode == ToolApprovalMode.NeverRequire)
        {
            return [];
        }

        if (options.Mode == ToolApprovalMode.AlwaysRequire)
        {
            return
            [
                new ToolPermissionRule
                {
                    ToolPattern = "*",
                    Behavior = PermissionBehavior.Ask,
                    Scope = ToolPermissionScope.System,
                    Priority = -100,
                    Reason = "Approval required by configuration"
                }
            ];
        }

        var rules = new List<ToolPermissionRule>();

        foreach (var toolName in options.AlwaysRequireApproval
                     .Select(NormalizeOptional)
                     .Where(x => x != null)
                     .Cast<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            rules.Add(new ToolPermissionRule
            {
                ToolPattern = toolName,
                Behavior = PermissionBehavior.Ask,
                Scope = ToolPermissionScope.System,
                Priority = 100,
                Reason = "Approval required by configuration"
            });
        }

        foreach (var groupName in options.AlwaysRequireApprovalGroups
                     .Select(NormalizeOptional)
                     .Where(x => x != null)
                     .Cast<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            rules.Add(new ToolPermissionRule
            {
                ToolPattern = "*",
                ToolGroup = groupName,
                Behavior = PermissionBehavior.Ask,
                Scope = ToolPermissionScope.System,
                Priority = 90,
                Reason = "Approval required by configuration"
            });
        }

        foreach (var toolName in options.NeverRequireApproval
                     .Select(NormalizeOptional)
                     .Where(x => x != null)
                     .Cast<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            rules.Add(new ToolPermissionRule
            {
                ToolPattern = toolName,
                Behavior = PermissionBehavior.Allow,
                Scope = ToolPermissionScope.System,
                Priority = 80
            });
        }

        return rules;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
