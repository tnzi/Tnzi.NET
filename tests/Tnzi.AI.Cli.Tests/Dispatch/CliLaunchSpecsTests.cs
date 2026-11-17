namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// Whether a live process can take a turn meant for a new launch (文档 §6.18): the launch environment's run-scoped
/// credentials compare by the identity the consumer gives them, everything else by value.
/// </summary>
public class CliLaunchSpecsTests
{
    private const string Token = "ANTHROPIC_AUTH_TOKEN";

    private static readonly IReadOnlyDictionary<string, string?> None = new Dictionary<string, string?>();

    private static CliProcessSpec Spec(params (string Name, string Value)[] environment) => new()
    {
        ExecutablePath = "/usr/bin/claude",
        Arguments = ["--print", "--model", "m"],
        WorkingDirectory = "/work",
        Environment = environment.ToDictionary(e => e.Name, e => e.Value, StringComparer.OrdinalIgnoreCase)
    };

    private static Dictionary<string, string?> Scoped(string name, string? identity) => new() { [name] = identity };

    [Fact]
    public void ARunScopedVariable_WithTheSameIdentity_IsTheSameLaunch()
        => CliLaunchSpecs.SameLaunch(
                Spec((Token, "a"), ("CLAUDE_CONFIG_DIR", "/cfg")), Scoped(Token, "gateway:platform"),
                Spec((Token, "b"), ("CLAUDE_CONFIG_DIR", "/cfg")), Scoped(Token, "gateway:platform"))
            .ShouldBeTrue();

    [Fact]
    public void ARunScopedVariable_WithNoIdentityOnEitherSide_IsTheSameLaunch()
        => CliLaunchSpecs.SameLaunch(Spec((Token, "a")), Scoped(Token, null), Spec((Token, "b")), Scoped(Token, null)).ShouldBeTrue();

    [Fact]
    public void ARunScopedVariable_WithAnotherIdentity_IsAnotherLaunch()
    {
        CliLaunchSpecs.SameLaunch(Spec((Token, "a")), Scoped(Token, "gateway:platform"), Spec((Token, "a")), Scoped(Token, "gateway:own:1"))
            .ShouldBeFalse("the identity decides, even when the bytes happen to match");
        CliLaunchSpecs.SameLaunch(Spec((Token, "a")), Scoped(Token, null), Spec((Token, "b")), Scoped(Token, "gateway:platform"))
            .ShouldBeFalse();
    }

    [Fact]
    public void AnotherEnvironmentDifference_IsStillAnotherLaunch()
        => CliLaunchSpecs.SameLaunch(
                Spec((Token, "a"), ("CLAUDE_CONFIG_DIR", "/cfg")), Scoped(Token, "gateway:platform"),
                Spec((Token, "b"), ("CLAUDE_CONFIG_DIR", "/other")), Scoped(Token, "gateway:platform"))
            .ShouldBeFalse();

    [Fact]
    public void ARunScopedVariable_PresentOnOnlyOneSide_IsAnotherLaunch()
        => CliLaunchSpecs.SameLaunch(Spec((Token, "a")), Scoped(Token, "gateway:platform"), Spec(), Scoped(Token, "gateway:platform"))
            .ShouldBeFalse();

    [Fact]
    public void AVariableMarkedRunScopedOnOnlyOneSide_ComparesByValue()
    {
        CliLaunchSpecs.SameLaunch(Spec((Token, "a")), Scoped(Token, "gateway:platform"), Spec((Token, "b")), None).ShouldBeFalse();
        CliLaunchSpecs.SameLaunch(Spec((Token, "a")), None, Spec((Token, "a")), Scoped(Token, "gateway:platform")).ShouldBeTrue();
    }

    [Fact]
    public void WithoutRunScopedVariables_TheEnvironmentComparesByValue()
    {
        CliLaunchSpecs.SameLaunch(Spec((Token, "a")), None, Spec((Token, "a")), None).ShouldBeTrue();
        CliLaunchSpecs.SameLaunch(Spec((Token, "a")), None, Spec((Token, "b")), None).ShouldBeFalse();
    }

    [Fact]
    public void RunScopedNames_MatchTheEnvironmentWithoutRegardToCase()
        => CliLaunchSpecs.SameLaunch(
                Spec((Token, "a")), new Dictionary<string, string?>(StringComparer.Ordinal) { ["anthropic_auth_token"] = "g" },
                Spec((Token, "b")), Scoped(Token, "g"))
            .ShouldBeTrue();

    [Fact]
    public void ResumeArguments_AreNotALaunchDifference()
        => CliLaunchSpecs.SameLaunch(
                Spec() with { Arguments = ["--print", "--resume", "s1"] }, None,
                Spec() with { Arguments = ["--print", "--resume", "s1", "--fork-session"] }, None)
            .ShouldBeTrue();

    /// <summary>
    /// The difference that ended a handed-over process is logged by where it is, never by value: the option whose value
    /// changed, or the names of the variables that differ or are on one side only (their values may be credentials).
    /// </summary>
    [Fact]
    public void TheDifference_NamesTheOptionAndTheVariables_WithoutTheirValues()
    {
        CliLaunchSpecs.FindDifference(Spec(), None, Spec() with { Arguments = ["--print", "--model", "bigger"] }, None)
            .ShouldBe("value of argument --model");
        CliLaunchSpecs.FindDifference(Spec(), None, Spec() with { Arguments = ["--print", "--model", "m", "--settings", "{}"] }, None)
            .ShouldBe("argument --settings");

        var difference = CliLaunchSpecs.FindDifference(
            Spec(("DATABASE_URL", "postgres://secret"), (Token, "a")), None,
            Spec((Token, "b"), ("GIT_AUTHOR_NAME", "someone")), None);
        difference.ShouldBe($"environment variables {Token}, DATABASE_URL, GIT_AUTHOR_NAME");
        difference.ShouldNotContain("secret");

        CliLaunchSpecs.FindDifference(Spec((Token, "a")), Scoped(Token, "g"), Spec((Token, "b")), Scoped(Token, "g")).ShouldBeNull();
    }

    private static CliProcessSpec WithArgs(params string[] arguments) => Spec() with { Arguments = arguments };

    private static readonly string[] Effort = ["--effort"];

    [Fact]
    public void ADifferentEffort_IsADifferentLaunch_UnlessTheProcessCanSwitchIt()
    {
        var high = WithArgs("--print", "--effort", "high");
        var low = WithArgs("--print", "--effort", "low");

        CliLaunchSpecs.FindDifference(high, None, low, None).ShouldBe("value of argument --effort");
        CliLaunchSpecs.SameLaunch(high, None, low, None, Effort).ShouldBeTrue();
    }

    [Fact]
    public void AnEffortOnOneSideOnly_StaysADifference_EvenWhenSwitchable()
    {
        // 换成「没有」回到的是模型的默认档，不是不带 --effort 启动的那个值：另起进程
        CliLaunchSpecs.SameLaunch(WithArgs("--print", "--effort", "high"), None, WithArgs("--print"), None, Effort).ShouldBeFalse();
        CliLaunchSpecs.SameLaunch(WithArgs("--print"), None, WithArgs("--print", "--effort", "low"), None, Effort).ShouldBeFalse();
    }

    [Fact]
    public void ASwitchableOption_DoesNotHideOtherDifferences()
        => CliLaunchSpecs.FindDifference(
                WithArgs("--model", "a", "--effort", "high"), None, WithArgs("--model", "b", "--effort", "low"), None, Effort)
            .ShouldBe("value of argument --model");
}
