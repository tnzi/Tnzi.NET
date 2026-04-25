namespace Tnzi.AI.Services;

public class NoOpSkillRequirementsValidator : ISkillRequirementsValidator, INoOpService
{
    public SkillValidationResult ValidateRequirements(SkillDefinition skill)
        => new() { IsValid = true };
}
