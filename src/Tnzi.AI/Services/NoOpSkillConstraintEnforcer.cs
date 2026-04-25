namespace Tnzi.AI.Services;

public class NoOpSkillConstraintEnforcer : ISkillConstraintEnforcer, INoOpService
{
    public SkillConstraintResult Apply(SkillDefinition skill, SkillConstraintContext context)
        => new()
        {
            EffectiveToolGroups = [..context.AvailableToolGroups],
            EffectiveModel = context.CurrentModel,
            EffectiveProvider = context.CurrentProvider
        };
}
