using Tnzi.AI.Coder.Repl;

namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// A6: Shell/REPL tools fail-closed when approval is required but no handler is wired.
/// </summary>
public class ShellToolsFailClosedTests
{
    private static IOptions<CoderOptions> CreateOptions() =>
        Microsoft.Extensions.Options.Options.Create(new CoderOptions
        {
            ProjectRoot = Path.GetTempPath(),
            Sandbox = new SandboxOptions
            {
                DeniedCommands = ["rm -rf /"],
                DefaultCommandTimeoutMs = 5000
            }
        });

    // ── ShellTools ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShellTools_RequiresApproval_NullHandler_ReturnsDenialResult_CommandNotExecuted()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous operation"));

        var pathValidator = new Mock<IPathValidator>();
        // no approvalHandler — null (default optional param)
        var tools = new ShellTools(
            sanitizer.Object,
            pathValidator.Object,
            CreateOptions(),
            Mock.Of<ILogger<ShellTools>>());
        // approvalHandler NOT passed → null

        var result = await tools.ExecuteAsync("dangerous-cmd");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("approval handler");
        // path validator should NOT have been called — command was denied before execution
        pathValidator.Verify(v => v.ValidateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ShellTools_RequiresApproval_WithAutoApproveHandler_ExecutesProceedsPast()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous operation"));

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.AutoApprove());

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "test-stop" });

        var tools = new ShellTools(
            sanitizer.Object,
            pathValidator.Object,
            CreateOptions(),
            Mock.Of<ILogger<ShellTools>>(),
            approvalHandler.Object);

        var result = await tools.ExecuteAsync("echo hello");

        var json = JsonSerializer.Serialize(result);
        // Got past approval (AutoApprove), hit working directory validation
        json.ShouldContain("test-stop");
        approvalHandler.Verify(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShellTools_RequiresApproval_NullHandler_StreamingAlsoDenied()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Streaming dangerous"));

        var pathValidator = new Mock<IPathValidator>();
        var tools = new ShellTools(
            sanitizer.Object,
            pathValidator.Object,
            CreateOptions(),
            Mock.Of<ILogger<ShellTools>>());

        var result = await tools.ExecuteStreamingAsync("some-cmd");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("approval handler");
    }

    // ── BashTools ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BashTools_RequiresApproval_NullHandler_ReturnsDenialResult()
    {
        if (OperatingSystem.IsWindows())
        {
            // BashTools returns platform error before approval check on Windows
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous"));

        var pathValidator = new Mock<IPathValidator>();
        var tools = new BashTools(
            sanitizer.Object,
            pathValidator.Object,
            new BashShellAdapter(),
            CreateOptions(),
            Mock.Of<ILogger<BashTools>>());
        // no approval handler

        var result = await tools.ExecuteBashAsync("dangerous-cmd");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("approval handler");
        pathValidator.Verify(v => v.ValidateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BashTools_RequiresApproval_WithAutoApproveHandler_Proceeds()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous"));

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.AutoApprove());

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "test-stop" });

        var tools = new BashTools(
            sanitizer.Object,
            pathValidator.Object,
            new BashShellAdapter(),
            CreateOptions(),
            Mock.Of<ILogger<BashTools>>(),
            approvalHandler.Object);

        var result = await tools.ExecuteBashAsync("echo hello");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("test-stop");
        approvalHandler.Verify(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── PowerShellTools ───────────────────────────────────────────────────────

    [Fact]
    public async Task PowerShellTools_RequiresApproval_NullHandler_ReturnsDenialResult()
    {
        if (!OperatingSystem.IsWindows())
        {
            // PowerShellTools returns platform error before approval check on non-Windows
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous"));

        var pathValidator = new Mock<IPathValidator>();
        var tools = new PowerShellTools(
            sanitizer.Object,
            pathValidator.Object,
            new PowerShellShellAdapter(),
            CreateOptions(),
            Mock.Of<ILogger<PowerShellTools>>());
        // no approval handler

        var result = await tools.ExecutePowerShellAsync("dangerous-cmd");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("approval handler");
        pathValidator.Verify(v => v.ValidateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PowerShellTools_RequiresApproval_WithAutoApproveHandler_Proceeds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous"));

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.AutoApprove());

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "test-stop" });

        var tools = new PowerShellTools(
            sanitizer.Object,
            pathValidator.Object,
            new PowerShellShellAdapter(),
            CreateOptions(),
            Mock.Of<ILogger<PowerShellTools>>(),
            approvalHandler.Object);

        var result = await tools.ExecutePowerShellAsync("echo hello");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("test-stop");
        approvalHandler.Verify(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ReplTools ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplTools_RequiresApproval_NullHandler_ReturnsDenialResult_CodeNotExecuted()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Code execution requires approval"));

        var tools = new ReplTools(
            sanitizer.Object,
            CreateOptions(),
            Mock.Of<ILogger<ReplTools>>());
        // no approval handler

        var result = await tools.ExecuteCodeAsync("print('hello')", "python");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("approval handler");
    }

    [Fact]
    public async Task ReplTools_RequiresApproval_WithAutoApproveHandler_ProceedsToExecution()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Code execution requires approval"));

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.AutoApprove());

        var tools = new ReplTools(
            sanitizer.Object,
            CreateOptions(),
            Mock.Of<ILogger<ReplTools>>(),
            approvalHandler.Object);

        // Even if no runtime found, the call must have passed the approval gate
        var result = await tools.ExecuteCodeAsync("print('hello')", "python");

        var json = JsonSerializer.Serialize(result);
        // Should NOT contain approval handler error — approval was granted
        json.ShouldNotContain("approval handler");
        approvalHandler.Verify(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Allow-flow unaffected (RequiresApproval=false, no handler) ─────────────

    [Fact]
    public async Task ShellTools_NoApprovalRequired_NullHandler_DoesNotDenyPrematurely()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.Allow());

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "test-stop" });

        var tools = new ShellTools(
            sanitizer.Object,
            pathValidator.Object,
            CreateOptions(),
            Mock.Of<ILogger<ShellTools>>());

        var result = await tools.ExecuteAsync("echo hello");

        var json = JsonSerializer.Serialize(result);
        // Got past approval-check (not required), hit working directory validation
        json.ShouldNotContain("approval handler");
        json.ShouldContain("test-stop");
    }
}
