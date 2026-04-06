namespace Tnzi.AI.Tests.Coder;

public class ShellAdapterTests
{
    [Fact]
    public void BashShellAdapter_CreateProcessStartInfo_UsesDashC()
    {
        var adapter = new BashShellAdapter();

        var psi = adapter.CreateProcessStartInfo("echo hello", AppContext.BaseDirectory);

        psi.FileName.ShouldBe(BashShellAdapter.DefaultExecutablePath);
        psi.ArgumentList.Count.ShouldBe(2);
        psi.ArgumentList[0].ShouldBe("-c");
        psi.ArgumentList[1].ShouldBe("echo hello");
    }

    [Fact]
    public void PowerShellShellAdapter_CreateProcessStartInfo_UsesCommandFlag()
    {
        var adapter = new PowerShellShellAdapter();

        var psi = adapter.CreateProcessStartInfo("Write-Output 'hello'", AppContext.BaseDirectory);

        // 应包含 -NoLogo -NoProfile -NonInteractive -Command <command>
        psi.ArgumentList.Count.ShouldBe(5);
        psi.ArgumentList[0].ShouldBe("-NoLogo");
        psi.ArgumentList[1].ShouldBe("-NoProfile");
        psi.ArgumentList[2].ShouldBe("-NonInteractive");
        psi.ArgumentList[3].ShouldBe("-Command");
        psi.ArgumentList[4].ShouldBe("Write-Output 'hello'");
    }

    [Fact]
    public void PowerShellShellAdapter_CreateProcessStartInfo_SetsWorkingDirectory()
    {
        var adapter = new PowerShellShellAdapter();
        var workDir = AppContext.BaseDirectory;

        var psi = adapter.CreateProcessStartInfo("Get-Location", workDir);

        psi.WorkingDirectory.ShouldBe(workDir);
        psi.RedirectStandardOutput.ShouldBeTrue();
        psi.RedirectStandardError.ShouldBeTrue();
        psi.UseShellExecute.ShouldBeFalse();
        psi.CreateNoWindow.ShouldBeTrue();
    }

    [Fact]
    public void PowerShellShellAdapter_CustomExecutablePath_UsesSpecifiedPath()
    {
        var customPath = "C:\\custom\\pwsh.exe";
        var adapter = new PowerShellShellAdapter(customPath);

        adapter.ExecutablePath.ShouldBe(customPath);
    }

    [Fact]
    public void PowerShellShellAdapter_CustomPwshPath_ReportsIsPwsh()
    {
        var adapter = new PowerShellShellAdapter("pwsh");

        adapter.IsPwsh.ShouldBeTrue();
        adapter.Name.ShouldBe("pwsh");
    }

    [Fact]
    public void PowerShellShellAdapter_WindowsPowerShellPath_ReportsNotPwsh()
    {
        var adapter = new PowerShellShellAdapter("powershell.exe");

        adapter.IsPwsh.ShouldBeFalse();
        adapter.Name.ShouldBe("powershell");
    }

    [Fact]
    public void BashShellAdapter_QuoteArgument_EscapesSingleQuotes()
    {
        var adapter = new BashShellAdapter();

        adapter.QuoteArgument("C:\\work dir\\it's").ShouldBe("'C:\\work dir\\it'\\''s'");
    }

    [Fact]
    public void PowerShellShellAdapter_QuoteArgument_EscapesSingleQuotes()
    {
        var adapter = new PowerShellShellAdapter();

        adapter.QuoteArgument("C:\\work dir\\it's").ShouldBe("'C:\\work dir\\it''s'");
    }

    [Fact]
    public void PowerShellShellAdapter_QuoteArgument_HandlesEmptyString()
    {
        var adapter = new PowerShellShellAdapter();

        adapter.QuoteArgument("").ShouldBe("''");
    }

    [Fact]
    public void PowerShellShellAdapter_QuoteArgument_HandlesMultipleSingleQuotes()
    {
        var adapter = new PowerShellShellAdapter();

        adapter.QuoteArgument("it's a 'test'").ShouldBe("'it''s a ''test'''");
    }

    [Fact]
    public void BashShellAdapter_Name_ReturnsBash()
    {
        var adapter = new BashShellAdapter();

        adapter.Name.ShouldBe("bash");
        adapter.ExecutablePath.ShouldBe("/bin/bash");
    }

    [Fact]
    public void ShellAdapterBase_CreateProcessStartInfo_ConfiguresRedirection()
    {
        var adapter = new BashShellAdapter();

        var psi = adapter.CreateProcessStartInfo("ls", "/tmp", redirectStandardOutput: false, redirectStandardError: false);

        psi.RedirectStandardOutput.ShouldBeFalse();
        psi.RedirectStandardError.ShouldBeFalse();
        psi.UseShellExecute.ShouldBeFalse();
        psi.CreateNoWindow.ShouldBeTrue();
    }

    [Fact]
    public void PowerShellShellAdapter_DefaultConstructor_ResolvesToValidPath()
    {
        var adapter = new PowerShellShellAdapter();

        // 默认构造函数自动检测：应为 pwsh 或 powershell.exe
        adapter.ExecutablePath.ShouldNotBeNullOrWhiteSpace();
        (adapter.Name == "pwsh" || adapter.Name == "powershell").ShouldBeTrue();
    }
}

public class PowerShellToolsTests
{
    private static IOptions<CoderOptions> CreateOptions(Action<CoderOptions>? configure = null)
    {
        var options = new CoderOptions
        {
            ProjectRoot = Path.GetTempPath(),
            Sandbox = new SandboxOptions
            {
                DeniedCommands = ["Remove-Item -Recurse -Force /", "Format-Volume"],
                DefaultCommandTimeoutMs = 5000
            }
        };
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    [Fact]
    public async Task ExecutePowerShell_OnNonWindows_ReturnsError()
    {
        if (OperatingSystem.IsWindows()) return; // 仅在非 Windows 上测试

        var tools = CreatePowerShellTools();
        var result = await tools.ExecutePowerShellAsync("Write-Output 'hello'");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("only available on Windows");
    }

    [Fact]
    public async Task ExecuteStreaming_OnNonWindows_ReturnsError()
    {
        if (OperatingSystem.IsWindows()) return; // 仅在非 Windows 上测试

        var tools = CreatePowerShellTools();
        var result = await tools.ExecuteStreamingAsync("Write-Output 'hello'");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("only available on Windows");
    }

    [Fact]
    public async Task ExecutePowerShell_DeniedCommand_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize("Remove-Item -Recurse -Force /"))
            .Returns(CommandSanitizeResult.Deny("Command is denied"));

        var tools = CreatePowerShellTools(commandSanitizer: sanitizer.Object);

        var result = await tools.ExecutePowerShellAsync("Remove-Item -Recurse -Force /");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("denied");
    }

    [Fact]
    public async Task ExecutePowerShell_ApprovalRejected_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.NeedsApproval("Dangerous operation"));

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler.Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Reject("User rejected"));

        var tools = CreatePowerShellTools(commandSanitizer: sanitizer.Object, approvalHandler: approvalHandler.Object);

        var result = await tools.ExecutePowerShellAsync("some-command");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("rejected");
    }

    [Fact]
    public async Task ExecutePowerShell_InvalidWorkingDirectory_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.Allow());

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync("/nonexistent"))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "Path not allowed" });

        var tools = CreatePowerShellTools(commandSanitizer: sanitizer.Object, pathValidator: pathValidator.Object);

        var result = await tools.ExecutePowerShellAsync("Write-Output 'test'", "/nonexistent");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("not allowed");
    }

    [Fact]
    public async Task ExecuteStreaming_DeniedCommand_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize("Format-Volume"))
            .Returns(CommandSanitizeResult.Deny("Command is denied"));

        var tools = CreatePowerShellTools(commandSanitizer: sanitizer.Object);

        var result = await tools.ExecuteStreamingAsync("Format-Volume");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("denied");
    }

    [Fact]
    public async Task ExecuteStreaming_InvalidWorkingDirectory_ReturnsError()
    {
        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.Allow());

        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync("/bad-dir"))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "Path not allowed" });

        var tools = CreatePowerShellTools(commandSanitizer: sanitizer.Object, pathValidator: pathValidator.Object);

        var result = await tools.ExecuteStreamingAsync("Get-Process", "/bad-dir");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("not allowed");
    }

    private static PowerShellTools CreatePowerShellTools(
        ICommandSanitizer? commandSanitizer = null,
        IPathValidator? pathValidator = null,
        IToolApprovalHandler? approvalHandler = null)
    {
        var sanitizer = commandSanitizer ?? new Mock<ICommandSanitizer>().Object;
        var validator = pathValidator ?? new Mock<IPathValidator>().Object;
        var adapter = new PowerShellShellAdapter("powershell.exe");

        return new PowerShellTools(
            sanitizer,
            validator,
            adapter,
            CreateOptions(),
            NullLogger<PowerShellTools>.Instance,
            approvalHandler);
    }
}
