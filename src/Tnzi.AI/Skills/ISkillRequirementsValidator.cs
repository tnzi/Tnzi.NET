namespace Tnzi.AI.Skills;

/// <summary>
/// Validates skill requirements (bins, envs, configs, os, toolGroups).
/// </summary>
public interface ISkillRequirementsValidator
{
    /// <summary>
    /// Validates whether the skill's requirements are met in the current environment.
    /// </summary>
    SkillValidationResult ValidateRequirements(SkillDefinition skill);
}
