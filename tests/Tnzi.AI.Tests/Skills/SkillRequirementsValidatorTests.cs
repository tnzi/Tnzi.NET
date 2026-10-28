namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// What <see cref="SkillRequirementsValidator"/> may ask the host, and for whom.
/// </summary>
/// <remarks>
/// Activation reports every missing binary, environment variable and configuration key, so
/// each requirement is a question put to the host. A personal skill (any signed-in user can
/// write one) must not be able to ask it, and no skill may turn a "binary" into a path check.
/// </remarks>
public class SkillRequirementsValidatorTests
{
    private const string SetKey = "Payments:ApiKey";

    private static SkillRequirementsValidator CreateValidator()
    {
        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions { RequireChecksEnabled = true }
            }
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SetKey] = "configured" })
            .Build();

        return new SkillRequirementsValidator(options, configuration);
    }

    private static SkillDefinition Skill(SkillScope scope, SkillSource source, SkillRequirements requirements) => new()
    {
        Slug = "probe",
        Name = "Probe",
        Content = "x",
        Scope = scope,
        Source = source,
        Requirements = requirements
    };

    [Fact]
    public void PathAsBinary_IsNeverLookedUp_EvenOnASharedSkill()
    {
        // A file that certainly exists: the test assembly itself. Path.Combine returns a rooted
        // second argument unchanged, so the old lookup reported it as an installed program.
        var existingFile = typeof(SkillRequirementsValidatorTests).Assembly.Location;
        var skill = Skill(SkillScope.Tenant, SkillSource.Database, new SkillRequirements { Bins = [existingFile] });

        var result = CreateValidator().ValidateRequirements(skill);

        result.IsValid.ShouldBeFalse();
        result.MissingBins.ShouldContain(existingFile);
    }

    [Fact]
    public void PersonalDatabaseSkill_ConfigRequirement_IsNotProbed_AndCountsAsUnmet()
    {
        // The key IS configured; a personal skill still must not learn that.
        var skill = Skill(SkillScope.User, SkillSource.Database, new SkillRequirements { Configs = [SetKey] });

        var result = CreateValidator().ValidateRequirements(skill);

        result.IsValid.ShouldBeFalse();
        result.MissingConfigs.ShouldBe([SetKey]);
    }

    [Fact]
    public void PersonalDatabaseSkill_EnvRequirement_IsNotProbed_AndCountsAsUnmet()
    {
        var skill = Skill(SkillScope.User, SkillSource.Database, new SkillRequirements { Envs = ["PATH"] });

        var result = CreateValidator().ValidateRequirements(skill);

        result.IsValid.ShouldBeFalse();
        result.MissingEnvs.ShouldBe(["PATH"]);
    }

    [Fact]
    public void SharedDatabaseSkill_ConfigRequirement_IsProbed()
    {
        var skill = Skill(SkillScope.Tenant, SkillSource.Database, new SkillRequirements { Configs = [SetKey], Envs = ["PATH"] });

        CreateValidator().ValidateRequirements(skill).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void OperatorInstalledPersonalSkill_ConfigRequirement_IsProbed()
    {
        // A SKILL.md under a user's data path was installed by the operator, not written
        // through the self-service endpoint.
        var skill = Skill(SkillScope.User, SkillSource.FileSystem, new SkillRequirements { Configs = [SetKey] });

        CreateValidator().ValidateRequirements(skill).IsValid.ShouldBeTrue();
    }
}
