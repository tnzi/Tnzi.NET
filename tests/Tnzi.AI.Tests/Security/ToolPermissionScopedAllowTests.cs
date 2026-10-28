namespace Tnzi.AI.Tests.Security;

/// <summary>
/// 带范围的放行规则（CommandPrefix / PathPrefix 的 Allow）必须覆盖这次调用的<b>全部</b>片段与路径。
/// </summary>
/// <remarks>
/// 此前命令前缀与路径前缀都是「任一命中即命中」，且命令前缀不看词边界：
/// <c>bash Allow CommandPrefix=git</c> 放行 <c>git status; rm -rf data</c> 与 <c>gitx</c>，
/// <c>write_file Allow PathPrefix=/workspace</c> 被「真实路径在外面、再塞一个 working_directory 在里面」满足。
/// 一旦放行规则命中，破坏性工具的默认拒绝就不再生效。拒绝与询问规则仍是任一命中即命中。
/// </remarks>
public class ToolPermissionScopedAllowTests
{
    private static readonly ShellCommandAnalyzer Analyzer = new();

    private static readonly string Workspace = OperatingSystem.IsWindows() ? @"D:\sandbox\workspace" : "/sandbox/workspace";
    private static readonly string Outside = OperatingSystem.IsWindows() ? @"D:\sandbox\secrets\key.pem" : "/sandbox/secrets/key.pem";

    #region CommandPrefix

    [Theory]
    [InlineData("git status; rm -rf data")]
    [InlineData("git status\nrm -rf data")]
    [InlineData("git log $(rm -rf data)")]
    public void AllowCommandPrefix_ExtraDestructiveSegment_DoesNotAllow(string command)
    {
        var decision = Evaluate(Rule(PermissionBehavior.Allow, commandPrefix: "git"), ShellContext(command));

        decision.Behavior.ShouldBe(PermissionBehavior.Deny, command);
        decision.MatchedRulePattern.ShouldBeNull();
    }

    [Fact]
    public void AllowCommandPrefix_RequiresAWordBoundary()
    {
        var decision = Evaluate(Rule(PermissionBehavior.Allow, commandPrefix: "git"), ShellContext("gitx status"));

        decision.MatchedRulePattern.ShouldBeNull();
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("git status && git diff")]
    [InlineData("git")]
    public void AllowCommandPrefix_EverySegmentMatches_Allows(string command)
    {
        var decision = Evaluate(Rule(PermissionBehavior.Allow, commandPrefix: "git"), ShellContext(command));

        decision.Behavior.ShouldBe(PermissionBehavior.Allow);
        decision.MatchedRulePattern.ShouldBe("bash");
    }

    [Fact]
    public void AllowCommandPrefix_WithoutAnalyzedSegments_DoesNotAllow()
    {
        var decision = Evaluate(
            Rule(PermissionBehavior.Allow, commandPrefix: "git"),
            new ToolPermissionContext { ToolName = "bash", ToolGroup = "shell", ShellCommand = "git status; rm -rf data", IsDestructive = true });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Fact]
    public void DenyCommandPrefix_AnySegmentMatches_Denies()
    {
        var decision = Evaluate(Rule(PermissionBehavior.Deny, commandPrefix: "rm"), ShellContext("ls; rm x"));

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.MatchedRulePattern.ShouldBe("bash");
    }

    [Fact]
    public void AskCommandPrefix_AnySegmentMatches_Asks()
    {
        var decision = Evaluate(Rule(PermissionBehavior.Ask, commandPrefix: "npm"), ShellContext("ls && npm publish"));

        decision.Behavior.ShouldBe(PermissionBehavior.Ask);
    }

    #endregion

    #region PathPrefix

    [Fact]
    public void AllowPathPrefix_OnePathOutsideTheScope_DoesNotAllow()
    {
        var decision = Evaluate(
            Rule(PermissionBehavior.Allow, pathPrefix: Workspace, toolPattern: "write_file"),
            new ToolPermissionContext
            {
                ToolName = "write_file",
                IsDestructive = true,
                WorkingDirectory = Workspace,
                CandidatePaths = [Workspace, Outside]
            });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.MatchedPathPrefix.ShouldBeNull();
    }

    [Fact]
    public void AllowPathPrefix_EveryPathInsideTheScope_Allows()
    {
        var decision = Evaluate(
            Rule(PermissionBehavior.Allow, pathPrefix: Workspace, toolPattern: "write_file"),
            new ToolPermissionContext
            {
                ToolName = "write_file",
                IsDestructive = true,
                CandidatePaths = [Workspace, Path.Combine(Workspace, "out", "report.md")]
            });

        decision.Behavior.ShouldBe(PermissionBehavior.Allow);
        decision.MatchedPathPrefix.ShouldBe(Workspace);
    }

    [Fact]
    public void AllowPathPrefix_NoPathAtAll_DoesNotAllow()
    {
        var decision = Evaluate(
            Rule(PermissionBehavior.Allow, pathPrefix: Workspace, toolPattern: "write_file"),
            new ToolPermissionContext { ToolName = "write_file", IsDestructive = true });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Theory]
    [InlineData(PermissionBehavior.Deny)]
    [InlineData(PermissionBehavior.Ask)]
    public void DenyOrAskPathPrefix_AnyPathInsideTheScope_Matches(PermissionBehavior behavior)
    {
        var decision = Evaluate(
            Rule(behavior, pathPrefix: Workspace, toolPattern: "write_file"),
            new ToolPermissionContext { ToolName = "write_file", CandidatePaths = [Outside, Path.Combine(Workspace, "a.txt")] });

        decision.Behavior.ShouldBe(behavior);
        decision.MatchedPathPrefix.ShouldBe(Workspace);
    }

    [Fact]
    public async Task ApprovalWrapper_ModelSuppliedWorkingDirectoryInsideScope_DoesNotAuthorizeAPathOutside()
    {
        var inner = AIFunctionFactory.Create(
            (string path, string? working_directory) => "should-not-run",
            new AIFunctionFactoryOptions
            {
                Name = "write_file",
                Description = "Write a file",
                AdditionalProperties = new Dictionary<string, object?> { [ToolMetadataKeys.Destructive] = true }
            });
        var wrapper = new ApprovalToolWrapper(
            inner,
            new Mock<IToolApprovalHandler>(MockBehavior.Strict).Object,
            new ToolApprovalOptions(),
            new ToolPermissionEvaluator([Rule(PermissionBehavior.Allow, pathPrefix: Workspace, toolPattern: "write_file")]),
            toolGroup: "file");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["path"] = Outside,
            ["working_directory"] = Workspace
        }));

        result.ShouldBe("Tool call rejected: Destructive tool requires explicit allow");
    }

    #endregion

    #region Helpers

    private static ToolPermissionRule Rule(PermissionBehavior behavior, string? commandPrefix = null, string? pathPrefix = null, string toolPattern = "bash")
        => new()
        {
            ToolPattern = toolPattern,
            CommandPrefix = commandPrefix,
            PathPrefix = pathPrefix,
            Behavior = behavior
        };

    private static ToolPermissionContext ShellContext(string command)
    {
        var analysis = Analyzer.Analyze(command);
        return new ToolPermissionContext
        {
            ToolName = "bash",
            ToolGroup = "shell",
            ShellCommand = command,
            ShellSegments = analysis.Segments.Select(s => s.CommandText).ToList(),
            IsDestructive = analysis.IsDestructiveCandidate
        };
    }

    private static ToolPermissionDecision Evaluate(ToolPermissionRule rule, ToolPermissionContext context)
        => new ToolPermissionEvaluator([rule]).Evaluate(context);

    #endregion
}
