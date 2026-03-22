namespace Tnzi.AI.Skills;

/// <summary>
/// 技能约束执行器实现
/// </summary>
public class SkillConstraintEnforcer : ISkillConstraintEnforcer
{
    /// <inheritdoc/>
    public SkillConstraintResult Apply(SkillDefinition skill, SkillConstraintContext context)
    {
        var result = new SkillConstraintResult();

        // Tool group filtering: intersection if AllowedToolGroups is non-null and non-empty
        if (skill.AllowedToolGroups is { Count: > 0 })
        {
            result.EffectiveToolGroups = context.AvailableToolGroups
                .Where(g => skill.AllowedToolGroups.Contains(g, StringComparer.OrdinalIgnoreCase))
                .ToList();
            result.FilteredOutToolGroups = context.AvailableToolGroups
                .Except(result.EffectiveToolGroups, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            result.EffectiveToolGroups = [.. context.AvailableToolGroups];
        }

        // 单工具白名单/黑名单透传
        result.EffectiveTools = skill.AllowedTools;
        result.DeniedTools = skill.DeniedTools;

        // Model/Provider override
        result.EffectiveModel = skill.RequiredModel ?? context.CurrentModel;
        result.EffectiveProvider = skill.RequiredProvider ?? context.CurrentProvider;

        return result;
    }
}
