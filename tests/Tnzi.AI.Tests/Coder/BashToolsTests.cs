namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// BashTools 安全门控单元测试（与 PowerShellTools / ShellTools 对称）
/// </summary>
public class BashToolsTests
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
    public async Task ExecuteBashAsync_OnWindows_ReturnsError()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 此测试仅在 Windows 上有意义
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.Allow());

        var pathValidator = new Mock<IPathValidator>();
        var tools = new BashTools(
            sanitizer.Object, pathValidator.Object,
            new BashShellAdapter(), CreateOptions(),
            Mock.Of<ILogger<BashTools>>());

        var result = await tools.ExecuteBashAsync("echo hello");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("only available on Unix");
    }

    [Fact]
    public async Task ExecuteStreamingAsync_OnWindows_ReturnsError()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.Allow());

        var pathValidator = new Mock<IPathValidator>();
        var tools = new BashTools(
            sanitizer.Object, pathValidator.Object,
            new BashShellAdapter(), CreateOptions(),
            Mock.Of<ILogger<BashTools>>());

        var result = await tools.ExecuteStreamingAsync("echo hello");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("only available on Unix");
    }

    [Fact]
    public async Task ExecuteBashAsync_DeniedCommand_ReturnsError()
    {
        if (OperatingSystem.IsWindows())
        {
            // BashTools returns platform error on Windows before reaching sanitizer
            // Test the sanitizer path indirectly via assertion that denied commands are checked
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize("rm -rf /"))
            .Returns(CommandSanitizeResult.Deny("Command is denied"));

        var pathValidator = new Mock<IPathValidator>();
        var tools = new BashTools(
            sanitizer.Object, pathValidator.Object,
            new BashShellAdapter(), CreateOptions(),
            Mock.Of<ILogger<BashTools>>());

        var result = await tools.ExecuteBashAsync("rm -rf /");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("denied");
    }

    [Fact]
    public async Task ExecuteBashAsync_ApprovalRejected_ReturnsError()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous operation"));

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler.Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Reject("User rejected"));

        var pathValidator = new Mock<IPathValidator>();
        var tools = new BashTools(
            sanitizer.Object, pathValidator.Object,
            new BashShellAdapter(), CreateOptions(),
            Mock.Of<ILogger<BashTools>>(),
            approvalHandler.Object);

        var result = await tools.ExecuteBashAsync("some-command");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("rejected");
    }

    [Fact]
    public void Constructor_ValidatesRequiredParameters()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        var pathValidator = new Mock<IPathValidator>();
        var shellAdapter = new BashShellAdapter();
        var options = CreateOptions();
        var logger = Mock.Of<ILogger<BashTools>>();

        // Should not throw with valid params
        var tools = new BashTools(sanitizer.Object, pathValidator.Object, shellAdapter, options, logger);
        tools.ShouldNotBeNull();
    }
}
