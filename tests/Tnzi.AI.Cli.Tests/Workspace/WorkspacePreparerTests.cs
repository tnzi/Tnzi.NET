namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 工作区布置与回滚。
/// </summary>
public class WorkspacePreparerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "tnzi-cli-ws-" + Guid.NewGuid().ToString("N"));

    private readonly FileSystemWorkspacePreparer _preparer;

    public WorkspacePreparerTests()
    {
        Directory.CreateDirectory(_root);
        _preparer = new FileSystemWorkspacePreparer(
            new TestOptionsMonitor<CliAgentOptions>(new CliAgentOptions { Enabled = true, WorkspacesRoot = _root }),
            NullLogger<FileSystemWorkspacePreparer>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不该让测试红。
        }

        GC.SuppressFinalize(this);
    }

    private static CliRunContext Context(Guid runId, string? userWorkDirectory = null) => new()
    {
        RunId = runId,
        AgentId = Guid.NewGuid(),
        Provider = CliBuiltInProviders.All["claude"],
        StableBrief = "# Agent\n\nDo the thing.\n",
        WorkDirectoryMode = userWorkDirectory is null
            ? CliWorkDirectoryMode.PerThread
            : CliWorkDirectoryMode.UserProvided,
        UserWorkDirectory = userWorkDirectory
    };

    private CliRunContext ThreadedContext(Guid runId, Guid? threadId, CliWorkDirectoryMode mode) => new()
    {
        RunId = runId,
        ThreadId = threadId,
        AgentId = Guid.NewGuid(),
        Provider = CliBuiltInProviders.All["claude"],
        StableBrief = "# Agent",
        WorkDirectoryMode = mode
    };

    /// <summary>
    /// PerThread: 同一线程的两轮落在同一个目录。
    /// </summary>
    /// <remarks>
    /// 这条就是「多轮对话能不能连续」本身。编码 CLI 按 cwd 存会话存档，两轮换了目录，
    /// 上一轮的 session id 在新目录里根本不存在，<c>--resume</c> 必被拒 ——
    /// 用户看到的就是 agent 完全不记得上一句话。
    /// </remarks>
    [Fact]
    public async Task PerThread_TwoRunsInOneThread_ShareTheSameWorkDirectory()
    {
        var threadId = Guid.NewGuid();

        var first = await _preparer.PrepareAsync(
            ThreadedContext(Guid.NewGuid(), threadId, CliWorkDirectoryMode.PerThread), CancellationToken.None);
        var second = await _preparer.PrepareAsync(
            ThreadedContext(Guid.NewGuid(), threadId, CliWorkDirectoryMode.PerThread), CancellationToken.None);

        second.WorkDirectory.ShouldBe(first.WorkDirectory);
    }

    /// <summary>PerThread 但没有线程（一次性任务）时按运行分 —— 那种运行本就没有下一轮。</summary>
    [Fact]
    public async Task PerThread_WithoutAThread_FallsBackToPerRun()
    {
        var a = await _preparer.PrepareAsync(
            ThreadedContext(Guid.NewGuid(), null, CliWorkDirectoryMode.PerThread), CancellationToken.None);
        var b = await _preparer.PrepareAsync(
            ThreadedContext(Guid.NewGuid(), null, CliWorkDirectoryMode.PerThread), CancellationToken.None);

        b.WorkDirectory.ShouldNotBe(a.WorkDirectory);
    }

    /// <summary>
    /// PerRun: 即便在同一线程里，每轮也是全新目录。
    /// </summary>
    /// <remarks>
    /// 不连续是这个模式存在的目的而不是副作用：它给的是「每次执行都从干净状态开始」。
    /// 如果它悄悄复用了线程目录，上一轮残留的文件会流进下一轮，
    /// 而那正是选这个模式的人要防的事。
    /// </remarks>
    [Fact]
    public async Task PerRun_TwoRunsInOneThread_GetSeparateWorkDirectories()
    {
        var threadId = Guid.NewGuid();

        var first = await _preparer.PrepareAsync(
            ThreadedContext(Guid.NewGuid(), threadId, CliWorkDirectoryMode.PerRun), CancellationToken.None);
        var second = await _preparer.PrepareAsync(
            ThreadedContext(Guid.NewGuid(), threadId, CliWorkDirectoryMode.PerRun), CancellationToken.None);

        second.WorkDirectory.ShouldNotBe(first.WorkDirectory);
    }

    /// <summary>
    /// 放宽同线程复用的同时，fail-closed 的内核必须原样保留：别的线程仍然拒绝。
    /// </summary>
    /// <remarks>
    /// 变的只是「归属者是谁」，不是「要不要判归属」。没有这条，
    /// 上一条测试可以靠把哨兵整个删掉来通过。
    /// </remarks>
    [Fact]
    public async Task ADifferentThread_IsStillRefused()
    {
        var shared = Guid.NewGuid();
        var first = ThreadedContext(Guid.NewGuid(), shared, CliWorkDirectoryMode.PerThread);
        await _preparer.PrepareAsync(first, CancellationToken.None);

        // 同一个目录（手工指到上一轮的工作目录），但归属另一个线程。
        var intruder = new CliRunContext
        {
            RunId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            Provider = CliBuiltInProviders.All["claude"],
            StableBrief = "# Agent",
            WorkDirectoryMode = CliWorkDirectoryMode.UserProvided,
            UserWorkDirectory = (await _preparer.PrepareAsync(first, CancellationToken.None)).WorkDirectory
        };

        await Should.ThrowAsync<InvalidOperationException>(
            () => _preparer.PrepareAsync(intruder, CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_CreatesDirectoryTreeAndBrief()
    {
        var workspace = await _preparer.PrepareAsync(Context(Guid.NewGuid()), CancellationToken.None);

        Directory.Exists(workspace.WorkDirectory).ShouldBeTrue();
        Directory.Exists(workspace.OutputDirectory).ShouldBeTrue();
        Directory.Exists(workspace.LogDirectory).ShouldBeTrue();
        File.Exists(Path.Combine(workspace.WorkDirectory, "CLAUDE.md")).ShouldBeTrue();

        // 身份哨兵：cwd 内一份 + 运行根一份（子进程逃逸到 cwd 上层仍落在受管区）。
        File.Exists(Path.Combine(workspace.WorkDirectory, ".tnzi", "run-marker.json")).ShouldBeTrue();
        File.Exists(Path.Combine(workspace.RootDirectory, "run-marker.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task Prepare_ProducesByteIdenticalBriefAcrossRuns()
    {
        // brief 落在 provider 的缓存前缀里。内容一变就作废整段历史的 prompt cache，
        // 续接一次的成本按整段上下文重算 —— 所以"稳定"是可测的硬要求，不是修辞。
        var agentId = Guid.NewGuid();

        var first = await _preparer.PrepareAsync(
            Context(Guid.NewGuid()) with { AgentId = agentId }, CancellationToken.None);
        var firstBytes = await File.ReadAllBytesAsync(Path.Combine(first.WorkDirectory, "CLAUDE.md"));

        var second = await _preparer.PrepareAsync(
            Context(Guid.NewGuid()) with { AgentId = agentId }, CancellationToken.None);
        var secondBytes = await File.ReadAllBytesAsync(Path.Combine(second.WorkDirectory, "CLAUDE.md"));

        secondBytes.ShouldBe(firstBytes);
    }

    [Fact]
    public async Task Prepare_WithUserWorkDirectory_DoesNotTruncateExistingMemoryFile()
    {
        var userRepo = Path.Combine(_root, "user-repo");
        Directory.CreateDirectory(userRepo);
        var memoryFile = Path.Combine(userRepo, "CLAUDE.md");
        await File.WriteAllTextAsync(memoryFile, "# My own notes\n\nkeep me\n");

        var workspace = await _preparer.PrepareAsync(
            Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        var content = await File.ReadAllTextAsync(memoryFile);
        content.ShouldStartWith("# My own notes\n\nkeep me\n");
        content.ShouldContain("Do the thing.");

        await _preparer.CleanupAsync(workspace, removeAll: false, CancellationToken.None);

        (await File.ReadAllTextAsync(memoryFile)).ShouldBe("# My own notes\n\nkeep me\n");
    }

    [Fact]
    public async Task Cleanup_WithRemoveAll_NeverDeletesUserProvidedWorkDirectory()
    {
        var userRepo = Path.Combine(_root, "user-repo-2");
        Directory.CreateDirectory(userRepo);
        await File.WriteAllTextAsync(Path.Combine(userRepo, "source.txt"), "precious");

        var workspace = await _preparer.PrepareAsync(
            Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        await _preparer.CleanupAsync(workspace, removeAll: true, CancellationToken.None);

        Directory.Exists(userRepo).ShouldBeTrue();
        File.Exists(Path.Combine(userRepo, "source.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Prepare_MaterializesSkillsIntoProviderNativeDirectory()
    {
        var context = Context(Guid.NewGuid()) with
        {
            Skills =
            [
                new CliSkillPayload { Slug = "code-review", Description = "Review a diff", Content = "Steps..." }
            ]
        };

        var workspace = await _preparer.PrepareAsync(context, CancellationToken.None);

        var skillFile = Path.Combine(workspace.WorkDirectory, ".claude", "skills", "code-review", "SKILL.md");
        File.Exists(skillFile).ShouldBeTrue();

        var content = await File.ReadAllTextAsync(skillFile);
        content.ShouldStartWith("---");
        content.ShouldContain("name: code-review");
        content.ShouldContain("Steps...");
    }

    [Fact]
    public async Task Prepare_WithTraversalSkillSlug_DoesNotEscapeTheSkillsDirectory()
    {
        // 技能 slug 来自数据库，可能是用户填的。不清洗的话一个 ../../ 就能把
        // 「往 skills 目录写文件」变成「往工作区外任意位置写文件」。
        var context = Context(Guid.NewGuid()) with
        {
            Skills = [new CliSkillPayload { Slug = "../../escaped", Content = "x" }]
        };

        var workspace = await _preparer.PrepareAsync(context, CancellationToken.None);

        File.Exists(Path.Combine(_root, "escaped", "SKILL.md")).ShouldBeFalse();
        var skillsRoot = Path.Combine(workspace.WorkDirectory, ".claude", "skills");
        Directory.EnumerateDirectories(skillsRoot).ShouldAllBe(d => Path.GetFileName(d) == "escaped");
    }

    [Fact]
    public async Task Prepare_WhenAnotherRunAlreadyClaimedTheDirectory_Throws()
    {
        var userRepo = Path.Combine(_root, "contended");
        Directory.CreateDirectory(userRepo);

        await _preparer.PrepareAsync(Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        // 别的运行还占着这个目录：两个 agent 同时在里面写文件会互相破坏工作成果。
        await Should.ThrowAsync<InvalidOperationException>(
            () => _preparer.PrepareAsync(Context(Guid.NewGuid(), userRepo), CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_WhenMarkerFileIsHalfWritten_ReclaimsTheDirectory()
    {
        // 半截写入的标记解析不了。那说明上一次运行崩在写标记的中途，
        // 不该因此永久锁死这个目录 —— 归属他人才拒绝，解析失败按自有回收。
        var userRepo = Path.Combine(_root, "half-written");
        Directory.CreateDirectory(Path.Combine(userRepo, ".tnzi"));
        await File.WriteAllTextAsync(Path.Combine(userRepo, ".tnzi", "run-marker.json"), "{ \"managedBy\": ");

        var workspace = await _preparer.PrepareAsync(Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        workspace.WorkDirectory.ShouldBe(userRepo);
    }
}

/// <summary>供测试使用的静态 <see cref="IOptionsMonitor{T}"/>。</summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
