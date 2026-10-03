using Tnzi.AI.Sandbox.Tools;
using static Tnzi.AI.Tests.Sandbox.SandboxTestSupport;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// bash 命令里 <c>/mnt/*</c> 的定位、越界判定与改写：物理根可以含空格（macOS 的
/// <c>~/Library/Application Support</c>、带空格的 Windows 用户名），越界守卫不能因此变弱。
/// </summary>
public class SandboxCommandPathsTests
{
    private static readonly string ThreadDirWithSpace =
        Path.Combine(Path.GetTempPath(), "Application Support", "Tnzi", "ai-threads", Guid.NewGuid().ToString("N"));

    private static string PosixRoot(string mount) => $"/Users/a b/Library/Application Support/Tnzi/ai-threads/t/{mount}";

    // ── 越界守卫 ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cat /mnt/workspace/a.txt")]
    [InlineData("python /mnt/workspace/build.py > /mnt/outputs/report.txt")]
    [InlineData("cat \"/mnt/workspace/my file.txt\"")]
    [InlineData("cat '/mnt/uploads/data set.csv'")]
    [InlineData("ls /mnt/workspace")]
    [InlineData("cat /mnt/workspace/../uploads/a.txt")]
    public void AllWithin_ThreadDirContainingSpaces_AcceptsInBoundsPaths(string command)
    {
        var tokens = SandboxCommandPaths.Find(command, SandboxShellDialect.Posix);

        tokens.ShouldNotBeEmpty();
        SandboxCommandPaths.AllWithin(tokens, ThreadDirWithSpace).ShouldBeTrue(command);
    }

    [Theory]
    [InlineData("cat /mnt/workspace/../../../etc/passwd")]
    [InlineData("cat \"/mnt/workspace/../../../etc/passwd\"")]
    [InlineData("cat '/mnt/workspace/../../..'/etc/passwd")]
    // 闭合引号之后的 /../.. 与前面是同一个 shell 词：shell 会把它们拼成一条路径，守卫也必须这么看。
    [InlineData("cat \"/mnt/workspace/a\"/../../../etc/passwd")]
    [InlineData("cat /mnt/workspace/a\\ b/../../../../etc/passwd")]
    [InlineData("echo ok && cat /mnt/outputs/../../../etc/passwd")]
    public void AllWithin_EscapingPaths_AreRefused_EvenWithSpacesInTheRoot(string command)
    {
        var tokens = SandboxCommandPaths.Find(command, SandboxShellDialect.Posix);

        SandboxCommandPaths.AllWithin(tokens, ThreadDirWithSpace).ShouldBeFalse(command);
    }

    [Fact]
    public void Find_WordEndsAtUnquotedBoundary_NotAtSpacesInsideQuotes()
    {
        var token = SandboxCommandPaths.Find("cat \"/mnt/workspace/my file.txt\" | wc -l", SandboxShellDialect.Posix).ShouldHaveSingleItem();

        token.Mount.ShouldBe("workspace");
        token.LiteralSuffix.ShouldBe("/my file.txt");
        token.Context.ShouldBe(SandboxCommandPaths.QuoteContext.Double);
    }

    [Fact]
    public void Find_WithoutVirtualPaths_ReturnsNothing()
    {
        SandboxCommandPaths.Find("echo hello; ls /tmp", SandboxShellDialect.Posix).ShouldBeEmpty();
    }

    // ── 改写 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rewrite_UnquotedContext_SingleQuotesARootContainingSpaces()
    {
        const string command = "cat /mnt/workspace/*.txt > /mnt/outputs/all.txt";
        var tokens = SandboxCommandPaths.Find(command, SandboxShellDialect.Posix);

        var rewritten = SandboxCommandPaths.Rewrite(command, tokens, SandboxShellDialect.Posix, PosixRoot);

        // 只引根：agent 写的 *.txt 仍在引号外，glob 照常展开。
        rewritten.ShouldBe(
            "cat '/Users/a b/Library/Application Support/Tnzi/ai-threads/t/workspace'/*.txt > " +
            "'/Users/a b/Library/Application Support/Tnzi/ai-threads/t/outputs'/all.txt");
    }

    [Fact]
    public void Rewrite_InsideDoubleQuotes_DoesNotNestSingleQuotes()
    {
        const string command = "cat \"/mnt/workspace/my file.txt\"";
        var tokens = SandboxCommandPaths.Find(command, SandboxShellDialect.Posix);

        SandboxCommandPaths.Rewrite(command, tokens, SandboxShellDialect.Posix, PosixRoot)
            .ShouldBe("cat \"/Users/a b/Library/Application Support/Tnzi/ai-threads/t/workspace/my file.txt\"");
    }

    [Fact]
    public void Rewrite_InsideSingleQuotes_EscapesOnlyEmbeddedQuotes()
    {
        const string command = "cat '/mnt/workspace/a.txt'";
        var tokens = SandboxCommandPaths.Find(command, SandboxShellDialect.Posix);

        SandboxCommandPaths.Rewrite(command, tokens, SandboxShellDialect.Posix, _ => "/home/o'neil/t/workspace")
            .ShouldBe("cat '/home/o'\\''neil/t/workspace/a.txt'");
    }

    [Fact]
    public void Rewrite_SafeRoot_IsLeftUnquoted()
    {
        const string command = "python /mnt/workspace/build.py";
        var tokens = SandboxCommandPaths.Find(command, SandboxShellDialect.Posix);

        SandboxCommandPaths.Rewrite(command, tokens, SandboxShellDialect.Posix, m => $"/workspace/{m}")
            .ShouldBe("python /workspace/workspace/build.py");
    }

    [Fact]
    public void Rewrite_Cmd_DoubleQuotesARootContainingSpaces()
    {
        const string command = "type /mnt/workspace/a.txt";
        var tokens = SandboxCommandPaths.Find(command, SandboxShellDialect.Cmd);

        SandboxCommandPaths.Rewrite(command, tokens, SandboxShellDialect.Cmd, m => $@"C:\Users\Jane Doe\AppData\Local\Tnzi\ai-threads\t\{m}")
            .ShouldBe(@"type ""C:\Users\Jane Doe\AppData\Local\Tnzi\ai-threads\t\workspace""/a.txt");
    }

    // ── 经 SandboxTools 的整条路径 ─────────────────────────────────────────

    [Fact]
    public async Task Bash_DataRootWithSpaces_ReachesTheSandboxAsOneQuotedWord()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tnzi space root {Guid.NewGuid():N}");
        try
        {
            var translator = new VirtualPathTranslator(dataRoot);
            var threadId = Guid.NewGuid();
            var threadDir = translator.GetThreadDirectory(threadId);
            var accessor = new AgentExecutionContextAccessor();
            var sandbox = new CountingSandbox("space", Path.Combine(threadDir, "workspace"));
            accessor.Properties[SandboxPropertyKeys.ToolEnvironment] = new SandboxToolEnvironment(sandbox, threadId);
            var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance, accessor);

            var result = JsonSerializer.Serialize(await tools.BashAsync("cat /mnt/workspace/a.txt"));

            result.ShouldNotContain("Command denied");
            sandbox.ExecutedCommands.ShouldHaveSingleItem()
                .ShouldBe($"cat '{Path.Combine(threadDir, "workspace")}'/a.txt");
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Bash_DataRootWithSpaces_EscapeIsStillRefused()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tnzi space root {Guid.NewGuid():N}");
        var translator = new VirtualPathTranslator(dataRoot);
        var threadId = Guid.NewGuid();
        var accessor = new AgentExecutionContextAccessor();
        var sandbox = new CountingSandbox("space", Path.Combine(translator.GetThreadDirectory(threadId), "workspace"));
        accessor.Properties[SandboxPropertyKeys.ToolEnvironment] = new SandboxToolEnvironment(sandbox, threadId);
        var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance, accessor);

        var result = JsonSerializer.Serialize(await tools.BashAsync("cat \"/mnt/workspace/x\"/../../../etc/passwd"));

        result.ShouldContain("Command denied");
        sandbox.ExecutedCommands.ShouldBeEmpty();
    }

    /// <summary>真实 shell 执行：含空格的线程目录里的文件能被命令读到（Windows 上经 cmd，其余经 bash）。</summary>
    [Fact]
    public async Task Bash_LocalSandbox_DataRootWithSpaces_ReadsTheFile()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tnzi space root {Guid.NewGuid():N}");
        var translator = new VirtualPathTranslator(dataRoot);
        var threadId = Guid.NewGuid();
        translator.EnsureThreadDirectories(threadId);
        var workspace = Path.Combine(translator.GetThreadDirectory(threadId), "workspace");
        await File.WriteAllTextAsync(Path.Combine(workspace, "a.txt"), "space-ok");
        await using var sandbox = new LocalSandbox("local-space", workspace, TimeSpan.FromSeconds(20), 64 * 1024);
        var accessor = new AgentExecutionContextAccessor();
        accessor.Properties[SandboxPropertyKeys.ToolEnvironment] = new SandboxToolEnvironment(sandbox, threadId);
        var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance, accessor);

        try
        {
            var command = OperatingSystem.IsWindows() ? "more < /mnt/workspace/a.txt" : "cat /mnt/workspace/a.txt";
            var result = JsonSerializer.Serialize(await tools.BashAsync(command));

            result.ShouldContain("space-ok");
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
        }
    }
}
