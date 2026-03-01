namespace Tnzi.AI.Tests;

/// <summary>
/// ShellTools 安全门控单元测试
/// </summary>
public class ShellToolsTests
{
    private static IOptions<CoderOptions> CreateOptions(Action<CoderOptions>? configure = null)
    {
        var options = new CoderOptions
        {
            ProjectRoot = Path.GetTempPath(),
            Sandbox = new SandboxOptions
            {
                DeniedCommands = ["rm -rf /", "format c:", "mkfs"],
                DefaultCommandTimeoutMs = 5000
            }
        };
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    [Fact]
    public async Task Execute_DeniedCommand_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize("rm -rf /"))
            .Returns(CommandSanitizeResult.Deny("Command is denied"));

        var pathValidator = new Mock<IPathValidator>();
        var tools = new ShellTools(sanitizer.Object, pathValidator.Object, CreateOptions(),
            Mock.Of<ILogger<ShellTools>>());

        var result = await tools.ExecuteAsync("rm -rf /");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("denied");
    }

    [Fact]
    public async Task Execute_ApprovalRejected_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous operation"));

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler.Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Reject("User rejected"));

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = true, ResolvedPath = Path.GetTempPath() });

        var tools = new ShellTools(sanitizer.Object, pathValidator.Object, CreateOptions(),
            Mock.Of<ILogger<ShellTools>>(), approvalHandler.Object);

        var result = await tools.ExecuteAsync("some-command");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("rejected");
    }

    [Fact]
    public async Task Execute_InvalidWorkingDirectory_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.Allow());

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync("/nonexistent/path"))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "Path not allowed" });

        var tools = new ShellTools(sanitizer.Object, pathValidator.Object, CreateOptions(),
            Mock.Of<ILogger<ShellTools>>());

        var result = await tools.ExecuteAsync("echo hello", "/nonexistent/path");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("not allowed");
    }

    [Fact]
    public async Task Execute_EmptyCommand_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        var pathValidator = new Mock<IPathValidator>();

        var tools = new ShellTools(sanitizer.Object, pathValidator.Object, CreateOptions(),
            Mock.Of<ILogger<ShellTools>>());

        var result = await tools.ExecuteAsync("  ");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
    }

    [Fact]
    public void CommandSanitizeResult_Deny_HasCorrectState()
    {
        var result = CommandSanitizeResult.Deny("forbidden");

        result.IsAllowed.ShouldBeFalse();
        result.RequiresApproval.ShouldBeFalse();
        result.Reason.ShouldBe("forbidden");
    }

    [Fact]
    public void CommandSanitizeResult_NeedsApproval_HasCorrectState()
    {
        var result = CommandSanitizeResult.NeedsApproval("dangerous");

        result.IsAllowed.ShouldBeTrue();
        result.RequiresApproval.ShouldBeTrue();
        result.Reason.ShouldBe("dangerous");
    }

    [Fact]
    public void CommandSanitizeResult_Allow_HasCorrectState()
    {
        var result = CommandSanitizeResult.Allow();

        result.IsAllowed.ShouldBeTrue();
        result.RequiresApproval.ShouldBeFalse();
        result.Reason.ShouldBeNull();
    }

    [Fact]
    public void ToolApprovalResult_FactoryMethods()
    {
        var approved = ToolApprovalResult.Approve("admin");
        approved.Approved.ShouldBeTrue();
        approved.Status.ShouldBe(ToolApprovalStatus.Approved);

        var rejected = ToolApprovalResult.Reject("not allowed");
        rejected.Approved.ShouldBeFalse();
        rejected.Status.ShouldBe(ToolApprovalStatus.Rejected);

        var timeout = ToolApprovalResult.Timeout();
        timeout.Approved.ShouldBeFalse();
        timeout.Status.ShouldBe(ToolApprovalStatus.Timeout);

        var auto = ToolApprovalResult.AutoApprove();
        auto.Approved.ShouldBeTrue();
        auto.Status.ShouldBe(ToolApprovalStatus.AutoApproved);
    }
}
