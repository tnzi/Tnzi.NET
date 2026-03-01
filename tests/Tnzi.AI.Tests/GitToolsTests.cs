using System.IO;

namespace Tnzi.AI.Tests;

/// <summary>
/// GitTools 安全与审批流单元测试
/// </summary>
public class GitToolsTests
{
    private static IOptions<CoderOptions> CreateOptions(string? projectRoot = null)
    {
        return Microsoft.Extensions.Options.Options.Create(new CoderOptions
        {
            ProjectRoot = projectRoot ?? Path.GetTempPath(),
            Sandbox = new SandboxOptions()
        });
    }

    private static GitTools CreateGitTools(
        IPathValidator? pathValidator = null,
        ICommandSanitizer? sanitizer = null,
        IToolApprovalHandler? approvalHandler = null,
        string? projectRoot = null)
    {
        return new GitTools(
            pathValidator ?? Mock.Of<IPathValidator>(),
            sanitizer ?? Mock.Of<ICommandSanitizer>(),
            CreateOptions(projectRoot),
            Mock.Of<ILogger<GitTools>>(),
            approvalHandler);
    }

    [Theory]
    [InlineData("main;rm -rf /")]
    [InlineData("branch|malicious")]
    [InlineData("name&command")]
    [InlineData("$(whoami)")]
    [InlineData("branch`id`")]
    public async Task GitCheckout_InjectionInRefName_ReturnsError(string target)
    {
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = true, ResolvedPath = Path.GetTempPath() });

        var tools = CreateGitTools(pathValidator: pathValidator.Object);

        var result = await tools.GitCheckoutAsync(target);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
    }

    [Theory]
    [InlineData("feature/my-branch")]
    [InlineData("main")]
    [InlineData("v1.0.0")]
    [InlineData("HEAD~3")]
    [InlineData("abc123")]
    public async Task GitCheckout_ValidRefName_PassesValidation(string target)
    {
        // 只验证 ref name 不报注入错误（可能因非 git 目录失败，但不是注入错误）
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = true, ResolvedPath = Path.GetTempPath() });

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler.Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Approve());

        var tools = CreateGitTools(pathValidator: pathValidator.Object, approvalHandler: approvalHandler.Object);

        var result = await tools.GitCheckoutAsync(target);
        var json = JsonSerializer.Serialize(result);

        // 不应包含注入相关错误
        json.ShouldNotContain("injection");
        json.ShouldNotContain("Invalid ref name");
    }

    [Fact]
    public async Task GitCommit_ApprovalRejected_ReturnsError()
    {
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = true, ResolvedPath = Path.GetTempPath() });

        var sanitizer = new Mock<ICommandSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns(CommandSanitizeResult.Allow());

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler.Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Reject("User rejected commit"));

        var tools = CreateGitTools(pathValidator: pathValidator.Object, sanitizer: sanitizer.Object, approvalHandler: approvalHandler.Object);

        var result = await tools.GitCommitAsync("test commit");

        var json = JsonSerializer.Serialize(result);
        // 写操作被拒绝时应返回包含 "rejected" 或 "error" 的结果
        (json.Contains("rejected") || json.Contains("error")).ShouldBeTrue();
    }

    [Fact]
    public async Task GitDiff_InvalidPath_ReturnsError()
    {
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync("/forbidden/path"))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "Path not in allowed directories" });

        var tools = CreateGitTools(pathValidator: pathValidator.Object);

        var result = await tools.GitDiffAsync("/forbidden/path");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("not in allowed");
    }

    [Theory]
    [InlineData("branch;drop")]
    [InlineData("name$(cmd)")]
    public async Task GitBranch_InjectionInBranchName_ReturnsError(string branchName)
    {
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = true, ResolvedPath = Path.GetTempPath() });

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler.Setup(h => h.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Approve());

        var tools = CreateGitTools(pathValidator: pathValidator.Object, approvalHandler: approvalHandler.Object);

        var result = await tools.GitBranchAsync(branchName);

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
    }
}
