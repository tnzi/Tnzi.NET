namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// PATH 查找：Windows 上 npm 安装的 CLI 必须解析到能被 <c>CreateProcess</c> 启动的那个垫片。
/// </summary>
/// <remarks>
/// npm 在 Windows 上全局安装时一次写三个垫片：<c>qwen</c>（无扩展名的 sh 脚本）、<c>qwen.cmd</c>、<c>qwen.ps1</c>。
/// 此前先试裸名，命中那个 sh 脚本：探测照样登记 Online，每次运行都以 LaunchFailed 结束。
/// 平台以参数传入，这组用例在任何系统上都跑同一套判定。
/// </remarks>
public class CliExecutableResolverTests : IDisposable
{
    // 小写：测试在大小写敏感的文件系统上也要跑，命中与否不能取决于宿主文件系统的大小写规则
    private const string WindowsPathExt = ".com;.exe;.bat;.cmd";

    private readonly string _bin = TestScratch.NewPath("path");

    public CliExecutableResolverTests() => Directory.CreateDirectory(_bin);

    public void Dispose()
    {
        TestScratch.Delete(_bin);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Windows_AnNpmInstall_ResolvesToTheCmdShim_NotTheShScript()
    {
        Touch("qwen");
        Touch("qwen.cmd");
        Touch("qwen.ps1");

        var resolved = CliExecutableResolver.SearchPath("qwen", _bin, WindowsPathExt, isWindows: true);

        resolved.ShouldBe(Path.Combine(_bin, "qwen.cmd"));
    }

    [Fact]
    public void Windows_AnExtensionlessFileAlone_IsNotAnExecutable()
    {
        Touch("qwen");

        CliExecutableResolver.SearchPath("qwen", _bin, WindowsPathExt, isWindows: true).ShouldBeNull();
    }

    [Fact]
    public void Windows_ANameThatAlreadyHasAnExecutableExtension_IsTriedAsIs()
    {
        Touch("node.exe");

        CliExecutableResolver.SearchPath("node.exe", _bin, WindowsPathExt, isWindows: true)
            .ShouldBe(Path.Combine(_bin, "node.exe"));
    }

    [Fact]
    public void Windows_WithoutPathExt_FallsBackToTheDefaultExtensions()
    {
        Touch("claude.EXE");

        CliExecutableResolver.SearchPath("claude", _bin, pathExt: null, isWindows: true)
            .ShouldBe(Path.Combine(_bin, "claude.EXE"));
    }

    [Fact]
    public void Unix_AnExtensionlessExecutable_StillResolves()
    {
        Touch("qwen");

        CliExecutableResolver.SearchPath("qwen", _bin, pathExt: null, isWindows: false)
            .ShouldBe(Path.Combine(_bin, "qwen"));
    }

    /// <summary>
    /// 解析到 <c>.cmd</c> 之后，参数会被 cmd.exe 再解析一遍：一个不带空格的 <c>&amp;</c> 就是命令分隔符。
    /// </summary>
    [Theory]
    [InlineData("--model=a&calc")]
    [InlineData("100%PATH%")]
    [InlineData("a|b")]
    [InlineData("say \"hi\"")]
    public void BatchFiles_RefuseArgumentsThatCmdWouldInterpret(string argument)
    {
        LocalProcessHost.FindUnsafeBatchArgument(Path.Combine(_bin, "qwen.cmd"), ["--print", argument])
            .ShouldBe(argument);
    }

    [Fact]
    public void BatchFiles_AcceptOrdinaryArguments_AndProgramsAcceptAnything()
    {
        LocalProcessHost.FindUnsafeBatchArgument(
                Path.Combine(_bin, "qwen.CMD"), ["--model", "claude-sonnet-4-5", "--mcp-config", @"C:\ws\a b\.tnzi\mcp.json"])
            .ShouldBeNull();
        LocalProcessHost.FindUnsafeBatchArgument(Path.Combine(_bin, "qwen.exe"), ["a&b"]).ShouldBeNull();
    }

    private void Touch(string name) => File.WriteAllText(Path.Combine(_bin, name), "#!/bin/sh\n");
}
