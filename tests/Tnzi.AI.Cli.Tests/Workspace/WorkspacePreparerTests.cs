namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 工作区布置与回滚。
/// </summary>
public class WorkspacePreparerTests : IDisposable
{
    private readonly string _root = TestScratch.NewPath("ws");

    private readonly FileSystemWorkspacePreparer _preparer;

    public WorkspacePreparerTests()
    {
        Directory.CreateDirectory(_root);
        var options = new CliAgentOptions { Enabled = true, WorkspacesRoot = _root };
        _preparer = new FileSystemWorkspacePreparer(
            new TestOptionsMonitor<CliAgentOptions>(options),
            NullLogger<FileSystemWorkspacePreparer>.Instance);
        Host = LocalHosts.Create(options);
    }

    /// <summary>本机执行目标（工作区根即本测试的临时目录）。</summary>
    private LocalProcessHost Host { get; }

    public void Dispose()
    {
        TestScratch.Delete(_root);

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

        var first = await _preparer.PrepareAsync(Host, 
            ThreadedContext(Guid.NewGuid(), threadId, CliWorkDirectoryMode.PerThread), CancellationToken.None);
        var second = await _preparer.PrepareAsync(Host, 
            ThreadedContext(Guid.NewGuid(), threadId, CliWorkDirectoryMode.PerThread), CancellationToken.None);

        second.WorkDirectory.ShouldBe(first.WorkDirectory);
    }

    /// <summary>PerThread 但没有线程（一次性任务）时按运行分 —— 那种运行本就没有下一轮。</summary>
    [Fact]
    public async Task PerThread_WithoutAThread_FallsBackToPerRun()
    {
        var a = await _preparer.PrepareAsync(Host, 
            ThreadedContext(Guid.NewGuid(), null, CliWorkDirectoryMode.PerThread), CancellationToken.None);
        var b = await _preparer.PrepareAsync(Host, 
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

        var first = await _preparer.PrepareAsync(Host, 
            ThreadedContext(Guid.NewGuid(), threadId, CliWorkDirectoryMode.PerRun), CancellationToken.None);
        var second = await _preparer.PrepareAsync(Host, 
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
        await _preparer.PrepareAsync(Host, first, CancellationToken.None);

        // 同一个目录（手工指到上一轮的工作目录），但归属另一个线程。
        var intruder = new CliRunContext
        {
            RunId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            Provider = CliBuiltInProviders.All["claude"],
            StableBrief = "# Agent",
            WorkDirectoryMode = CliWorkDirectoryMode.UserProvided,
            UserWorkDirectory = (await _preparer.PrepareAsync(Host, first, CancellationToken.None)).WorkDirectory
        };

        await Should.ThrowAsync<CliWorkspaceClaimedException>(
            () => _preparer.PrepareAsync(Host, intruder, CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_CreatesDirectoryTreeAndBrief()
    {
        var workspace = await _preparer.PrepareAsync(Host, Context(Guid.NewGuid()), CancellationToken.None);

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

        var first = await _preparer.PrepareAsync(Host, 
            Context(Guid.NewGuid()) with { AgentId = agentId }, CancellationToken.None);
        var firstBytes = await File.ReadAllBytesAsync(Path.Combine(first.WorkDirectory, "CLAUDE.md"));

        var second = await _preparer.PrepareAsync(Host, 
            Context(Guid.NewGuid()) with { AgentId = agentId }, CancellationToken.None);
        var secondBytes = await File.ReadAllBytesAsync(Path.Combine(second.WorkDirectory, "CLAUDE.md"));

        secondBytes.ShouldBe(firstBytes);
    }

    /// <summary>
    /// 用户提供的工作目录（团队仓库的一个 worktree）里一个受管文件都不出现：说明、技能、MCP 配置（带运行凭据）、占用标记都在它外面。
    /// </summary>
    /// <remarks>
    /// 此前说明注入用户的 <c>CLAUDE.md</c>、MCP 配置与标记写在仓库里的 <c>.tnzi/</c>：一次 <c>git add -A</c> 就连回写凭据一起提交出去。
    /// 断言整棵目录树逐字节不变，比逐个点名文件更不容易漏掉新加的那一种。
    /// </remarks>
    [Fact]
    public async Task Prepare_WithUserWorkDirectory_WritesNothingIntoIt()
    {
        var userRepo = Path.Combine(_root, "user-repo");
        Directory.CreateDirectory(userRepo);
        await File.WriteAllTextAsync(Path.Combine(userRepo, "CLAUDE.md"), "# My own notes\n\nkeep me\n");
        await File.WriteAllTextAsync(Path.Combine(userRepo, "source.txt"), "code");
        var before = Snapshot(userRepo);

        var workspace = await _preparer.PrepareAsync(Host, Context(Guid.NewGuid(), userRepo) with
        {
            Skills = [new CliSkillPayload { Slug = "code-review", Description = "Review a diff", Content = "Steps..." }],
            McpConfigJson = """{"mcpServers":{"tnzi":{"type":"http","url":"https://platform.example.com/mcp"}}}"""
        }, CancellationToken.None);

        Snapshot(userRepo).ShouldBe(before, "the user's directory is left exactly as it was");
        workspace.WorkDirectory.ShouldBe(userRepo);

        // 说明：运行根里的追加系统提示文件
        var systemPrompt = workspace.SystemPromptFilePath.ShouldNotBeNull();
        systemPrompt.ShouldStartWith(workspace.RootDirectory);
        (await File.ReadAllTextAsync(systemPrompt)).ShouldBe("# Agent\n\nDo the thing.\n");

        // 技能：运行根里的附加目录下，provider 自己的技能目录形状
        var contextDirectory = workspace.ContextDirectory.ShouldNotBeNull();
        contextDirectory.ShouldStartWith(workspace.RootDirectory);
        File.Exists(Path.Combine(contextDirectory, ".claude", "skills", "code-review", "SKILL.md")).ShouldBeTrue();

        // MCP 配置（回写凭据原文在里面）与占用标记都不在用户目录
        workspace.McpConfigPath.ShouldNotBeNull().ShouldStartWith(workspace.RootDirectory);
        var claim = CliWorkspaceLayout.ResolveClaimPath(Host, userRepo);
        File.Exists(claim).ShouldBeTrue();
        claim.ShouldNotStartWith(userRepo);

        await _preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);

        Snapshot(userRepo).ShouldBe(before);
        File.Exists(systemPrompt).ShouldBeFalse();
        File.Exists(workspace.McpConfigPath!).ShouldBeFalse("the credential goes when the run ends");
        Directory.Exists(contextDirectory).ShouldBeFalse();
        File.Exists(claim).ShouldBeFalse("the directory is free again");
    }

    /// <summary>
    /// provider 不能以文件接收追加系统提示、也不能从附加目录加载技能（内置的 ACP CLI）：照样不往用户目录写，
    /// 说明交给执行器内联进提示，技能不物化。
    /// </summary>
    [Fact]
    public async Task Prepare_WithUserWorkDirectory_ForAProviderWithoutOutOfTreeDelivery_StillWritesNothingIntoIt()
    {
        var userRepo = Path.Combine(_root, "user-repo-acp");
        Directory.CreateDirectory(userRepo);
        await File.WriteAllTextAsync(Path.Combine(userRepo, "AGENTS.md"), "# Team notes\n");
        var before = Snapshot(userRepo);

        var workspace = await _preparer.PrepareAsync(Host, Context(Guid.NewGuid(), userRepo) with
        {
            Provider = CliBuiltInProviders.All["qoder"],
            Skills = [new CliSkillPayload { Slug = "code-review", Content = "Steps..." }]
        }, CancellationToken.None);

        Snapshot(userRepo).ShouldBe(before);
        workspace.SystemPromptFilePath.ShouldBeNull();
        workspace.ContextDirectory.ShouldBeNull();
    }

    /// <summary>用户目录里一份旧编码的记忆文件不再是问题：框架根本不碰它。</summary>
    [Fact]
    public async Task Prepare_WithALegacyEncodedMemoryFileInAUserDirectory_LeavesItAlone()
    {
        var userRepo = Path.Combine(_root, "user-repo-gbk");
        Directory.CreateDirectory(userRepo);
        var memoryFile = Path.Combine(userRepo, "CLAUDE.md");
        byte[] gbk = [0xCF, 0xEE, 0xC4, 0xBF, 0xD4, 0xBC, 0xB6, 0xA8, 0x0A];
        await File.WriteAllBytesAsync(memoryFile, gbk);

        await _preparer.PrepareAsync(Host, Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        (await File.ReadAllBytesAsync(memoryFile)).ShouldBe(gbk);
        Directory.Exists(Path.Combine(userRepo, ".tnzi")).ShouldBeFalse();
    }

    /// <summary>
    /// 接回一个进程时认出用户目录模式的工作区：受管文件在运行根里；这一版之前起的进程，MCP 配置还在用户目录里，也认得出。
    /// </summary>
    [Fact]
    public async Task Open_UserProvided_FindsTheManagedFilesInTheRunRoot_AndTheLegacyLocation()
    {
        var userRepo = Path.Combine(_root, "user-repo-open");
        Directory.CreateDirectory(userRepo);
        var context = Context(Guid.NewGuid(), userRepo) with { McpConfigJson = """{"mcpServers":{}}""" };
        var prepared = await _preparer.PrepareAsync(Host, context, CancellationToken.None);

        var opened = (await _preparer.OpenAsync(Host, userRepo, context, CancellationToken.None)).ShouldNotBeNull();
        opened.McpConfigPath.ShouldBe(prepared.McpConfigPath);
        opened.SystemPromptFilePath.ShouldBe(prepared.SystemPromptFilePath);
        opened.WorkDirectoryIsUserOwned.ShouldBeTrue();

        // 旧版本的布局：MCP 配置在用户目录的 .tnzi 里
        File.Delete(prepared.McpConfigPath!);
        var legacy = Path.Combine(userRepo, CliWorkspaceLayout.MetadataDirectoryName, CliWorkspaceLayout.McpConfigFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        await File.WriteAllTextAsync(legacy, "{}");

        (await _preparer.OpenAsync(Host, userRepo, context, CancellationToken.None)).ShouldNotBeNull().McpConfigPath.ShouldBe(legacy);
    }

    /// <summary>目录下全部文件的相对路径与内容（比对「一个字节都没动」用）。</summary>
    private static Dictionary<string, string> Snapshot(string directory)
        => Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                entry => Path.GetRelativePath(directory, entry),
                entry => File.Exists(entry) ? Convert.ToBase64String(File.ReadAllBytes(entry)) : "<dir>");

    [Fact]
    public async Task Cleanup_WithRemoveAll_NeverDeletesUserProvidedWorkDirectory()
    {
        var userRepo = Path.Combine(_root, "user-repo-2");
        Directory.CreateDirectory(userRepo);
        await File.WriteAllTextAsync(Path.Combine(userRepo, "source.txt"), "precious");

        var workspace = await _preparer.PrepareAsync(Host, 
            Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        await _preparer.CleanupAsync(Host, workspace, removeAll: true, CancellationToken.None);

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

        var workspace = await _preparer.PrepareAsync(Host, context, CancellationToken.None);

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

        var workspace = await _preparer.PrepareAsync(Host, context, CancellationToken.None);

        File.Exists(Path.Combine(_root, "escaped", "SKILL.md")).ShouldBeFalse();
        var skillsRoot = Path.Combine(workspace.WorkDirectory, ".claude", "skills");
        Directory.EnumerateDirectories(skillsRoot).ShouldAllBe(d => Path.GetFileName(d) == "escaped");
    }

    [Fact]
    public async Task Prepare_WhenAnotherRunAlreadyClaimedTheDirectory_Throws()
    {
        var userRepo = Path.Combine(_root, "contended");
        Directory.CreateDirectory(userRepo);

        await _preparer.PrepareAsync(Host, Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        // 别的运行还占着这个目录：两个 agent 同时在里面写文件会互相破坏工作成果。
        var refused = await Should.ThrowAsync<CliWorkspaceClaimedException>(
            () => _preparer.PrepareAsync(Host, Context(Guid.NewGuid(), userRepo), CancellationToken.None));
        refused.OwnerRunId.ShouldNotBe(Guid.Empty, "the owner is reported so the caller can check whether it has finished");
    }

    /// <summary>
    /// 调度层确认占用者已经结束（崩溃残留）后，标明那一个运行的标记可回收：这次布置接管目录，结束时把标记删掉。
    /// </summary>
    [Fact]
    public async Task Prepare_WithTheCrashedOwnerMarkedReclaimable_TakesTheDirectoryOver()
    {
        var userRepo = Path.Combine(_root, "left-claimed");
        Directory.CreateDirectory(userRepo);
        var crashed = Context(Guid.NewGuid(), userRepo);
        await _preparer.PrepareAsync(Host, crashed, CancellationToken.None);

        // 只放行被点名的那一个运行
        await Should.ThrowAsync<CliWorkspaceClaimedException>(() => _preparer.PrepareAsync(Host, 
            Context(Guid.NewGuid(), userRepo) with { ReclaimableClaimRunId = Guid.NewGuid() }, CancellationToken.None));

        var next = Context(Guid.NewGuid(), userRepo) with { ReclaimableClaimRunId = crashed.RunId };
        var workspace = await _preparer.PrepareAsync(Host, next, CancellationToken.None);
        await _preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);

        File.Exists(CliWorkspaceLayout.ResolveClaimPath(Host, userRepo))
            .ShouldBeFalse("the taken-over marker is this run's now, and it is removed when the run ends");
    }

    /// <summary>
    /// 占用按目录本身记，不按写法：同一个用户目录换个写法（多一个尾部分隔符、<c>..</c> 绕回来）也认得出被占着。
    /// </summary>
    [Fact]
    public async Task Prepare_TheSameUserDirectorySpelledDifferently_IsStillClaimed()
    {
        var userRepo = Path.Combine(_root, "spelled");
        Directory.CreateDirectory(Path.Combine(userRepo, "sub"));
        await _preparer.PrepareAsync(Host, Context(Guid.NewGuid(), userRepo), CancellationToken.None);

        await Should.ThrowAsync<CliWorkspaceClaimedException>(() => _preparer.PrepareAsync(Host,
            Context(Guid.NewGuid(), Path.Combine(userRepo, "sub", "..")), CancellationToken.None));
    }

    // ── 重新执行时复用工作目录 ────────────────────────────────────────────────

    /// <summary>
    /// 打回队列后重新执行的运行，在原来的运行根上刷新，不另建按运行 id 的根。
    /// </summary>
    /// <remarks>
    /// 此前复用一律当成「用户提供的目录」重新布置：PerThread 每次重新执行都在 {tenant}/{runId} 多出一个游离目录，
    /// 里面的回收元数据标着「用户所有」，回收器从此只删产物与日志、目录本身永不回收。
    /// </remarks>
    [Fact]
    public async Task Reuse_PerThread_RefreshesTheOriginalRoot_WithoutAStrayRunRoot()
    {
        var runId = Guid.NewGuid();
        var context = ThreadedContext(runId, Guid.NewGuid(), CliWorkDirectoryMode.PerThread);
        var first = await _preparer.PrepareAsync(Host, context, CancellationToken.None);

        var reused = await _preparer.ReuseAsync(Host, first.WorkDirectory, context, CancellationToken.None);

        reused.ShouldNotBeNull();
        reused.RootDirectory.ShouldBe(first.RootDirectory);
        reused.WorkDirectory.ShouldBe(first.WorkDirectory);
        reused.OutputDirectory.ShouldBe(first.OutputDirectory);
        reused.WorkDirectoryIsUserOwned.ShouldBeFalse();
        Directory.Exists(Path.Combine(_root, "host", runId.ToString("N"))).ShouldBeFalse("no stray run root is created");
        ReadGcMetadata(first.RootDirectory).UserOwnedWorkDirectory.ShouldBeFalse();
    }

    /// <summary>
    /// PerRun 的运行根恰好按运行 id 定：此前复用把这个原根的回收元数据改成「用户所有」，整个工作目录永不回收。
    /// </summary>
    [Fact]
    public async Task Reuse_PerRun_KeepsTheWorkspaceManaged()
    {
        var context = ThreadedContext(Guid.NewGuid(), Guid.NewGuid(), CliWorkDirectoryMode.PerRun);
        var first = await _preparer.PrepareAsync(Host, context, CancellationToken.None);

        var reused = await _preparer.ReuseAsync(Host, first.WorkDirectory, context, CancellationToken.None);

        reused.ShouldNotBeNull();
        reused.RootDirectory.ShouldBe(first.RootDirectory);
        ReadGcMetadata(first.RootDirectory).UserOwnedWorkDirectory.ShouldBeFalse();
    }

    /// <summary>
    /// 用户目录模式：上一次执行崩在半路（没有清理），重新执行在同一个运行根上刷新，结束后运行根里的受管文件全部回滚，用户目录始终没动过。
    /// </summary>
    /// <remarks>
    /// 此前复用返回的运行根是用户仓库的上一级，清理按那里找不到的 sidecar 清单回滚，brief 块就留在了用户仓库里。
    /// </remarks>
    [Fact]
    public async Task Reuse_UserProvided_RefreshesTheRunRoot_AndNeverTouchesTheUsersDirectory()
    {
        var userRepo = Path.Combine(_root, "user-repo-reuse");
        Directory.CreateDirectory(userRepo);
        await File.WriteAllBytesAsync(Path.Combine(userRepo, "CLAUDE.md"), Encoding.UTF8.GetBytes("# My notes\n\nKeep this.\n"));
        var before = Snapshot(userRepo);
        var context = Context(Guid.NewGuid(), userRepo);

        // 第一次执行布置完就「崩溃」：没有清理
        var first = await _preparer.PrepareAsync(Host, context, CancellationToken.None);

        var reused = await _preparer.ReuseAsync(Host, userRepo, context, CancellationToken.None);
        reused.ShouldNotBeNull();
        reused.WorkDirectoryIsUserOwned.ShouldBeTrue();
        reused.RootDirectory.ShouldBe(first.RootDirectory);
        await _preparer.CleanupAsync(Host, reused, removeAll: false, CancellationToken.None);

        Snapshot(userRepo).ShouldBe(before);
        File.Exists(first.SystemPromptFilePath!).ShouldBeFalse();
        Directory.GetParent(userRepo)!.EnumerateFiles(CliWorkspaceLayout.GcMetadataFileName).ShouldBeEmpty(
            "nothing is written next to the user's repository");
    }

    /// <summary>认不出来的目录不复用：受管模式下不在工作区根之下的目录、用户目录模式下不是绑定配置的那个目录。</summary>
    [Fact]
    public async Task Reuse_ADirectoryThatDoesNotBelongToTheBinding_IsNotReused()
    {
        var elsewhere = Path.Combine(TestScratch.NewPath("not-managed"), CliWorkspaceLayout.WorkDirectoryName);
        Directory.CreateDirectory(elsewhere);
        try
        {
            (await _preparer.ReuseAsync(Host, 
                elsewhere, ThreadedContext(Guid.NewGuid(), Guid.NewGuid(), CliWorkDirectoryMode.PerThread), CancellationToken.None))
                .ShouldBeNull();

            var userRepo = Path.Combine(_root, "configured-repo");
            Directory.CreateDirectory(userRepo);
            (await _preparer.ReuseAsync(Host, elsewhere, Context(Guid.NewGuid(), userRepo), CancellationToken.None)).ShouldBeNull();

            Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(elsewhere)!).ShouldBe([elsewhere]);
        }
        finally
        {
            TestScratch.Delete(Path.GetDirectoryName(elsewhere)!);
        }
    }

    private static CliWorkspaceGcMetadata ReadGcMetadata(string root)
        => JsonSerializer.Deserialize<CliWorkspaceGcMetadata>(
            File.ReadAllText(Path.Combine(root, CliWorkspaceLayout.GcMetadataFileName)))!;

    [Fact]
    public async Task Prepare_WhenMarkerFileIsHalfWritten_ReclaimsTheDirectory()
    {
        // 半截写入的标记解析不了。那说明上一次运行崩在写标记的中途，
        // 不该因此永久锁死这个目录 —— 归属他人才拒绝，解析失败按自有回收。
        var userRepo = Path.Combine(_root, "half-written");
        Directory.CreateDirectory(Path.Combine(userRepo, ".tnzi"));
        await File.WriteAllTextAsync(Path.Combine(userRepo, ".tnzi", "run-marker.json"), "{ \"managedBy\": ");

        var workspace = await _preparer.PrepareAsync(Host, Context(Guid.NewGuid(), userRepo), CancellationToken.None);

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
