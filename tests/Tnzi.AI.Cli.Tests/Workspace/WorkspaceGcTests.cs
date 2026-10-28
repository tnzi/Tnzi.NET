namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 回收器的「终态且闲置」两半都要有真实依据：终态查运行表，闲置从最近一次活动算起。
/// </summary>
/// <remarks>
/// 此前回收只看运行根目录的修改时间。它只在根目录直接子项增删时变化：agent 在 <c>workdir/</c> 里干活不刷新它，
/// 覆盖写已存在的元数据也不刷新它，一次硬崩溃之后它就冻结了。于是一条跑了 12 小时以上的运行会被删掉可再生目录，
/// 一个仍在天天用的每线程工作区会在崩溃 24 小时后被整个删掉，哪怕此刻正有运行在里面执行。
/// </remarks>
public class WorkspaceGcTests : IDisposable
{
    private static readonly IReadOnlySet<Guid> NoActiveRuns = new HashSet<Guid>();

    private readonly string _root = TestScratch.NewPath("gc");

    public WorkspaceGcTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestScratch.Delete(_root);

        GC.SuppressFinalize(this);
    }

    private CliAgentOptions Options() => new()
    {
        Enabled = true,
        WorkspacesRoot = _root,
        Gc = { CompletedTtl = TimeSpan.FromHours(24), ArtifactTtl = TimeSpan.FromHours(12), OrphanTtl = TimeSpan.FromHours(72) }
    };

    private FileSystemWorkspacePreparer Preparer()
        => new(new TestOptionsMonitor<CliAgentOptions>(Options()), NullLogger<FileSystemWorkspacePreparer>.Instance);

    /// <summary>本机执行目标：回收器只管 API 进程所在机器上的工作区根。</summary>
    private LocalProcessHost Host => LocalHosts.Create(Options());

    private static CliRunContext Context(Guid runId, Guid threadId) => new()
    {
        RunId = runId,
        ThreadId = threadId,
        AgentId = Guid.NewGuid(),
        Provider = CliBuiltInProviders.All["claude"],
        StableBrief = "# Agent",
        WorkDirectoryMode = CliWorkDirectoryMode.PerThread
    };

    /// <summary>
    /// 一个仍在用的线程工作区：根目录的修改时间早已冻结（崩溃后再没有根级条目增删），
    /// 但运行一轮接一轮地在里面开始和结束。闲置要从最近一次运行结束算起。
    /// </summary>
    [Fact]
    public async Task AFrozenDirectoryTimestamp_DoesNotMakeARecentlyUsedWorkspaceIdle()
    {
        var preparer = Preparer();
        var workspace = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        File.WriteAllText(Path.Combine(workspace.WorkDirectory, "result.txt"), "the agent's work");
        await preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);

        var longAgo = DateTime.UtcNow.AddDays(-10);
        Directory.SetCreationTimeUtc(workspace.RootDirectory, longAgo);
        Directory.SetLastWriteTimeUtc(workspace.RootDirectory, longAgo);

        Gc().Collect(Options(), NoActiveRuns);

        File.Exists(Path.Combine(workspace.WorkDirectory, "result.txt")).ShouldBeTrue();
    }

    /// <summary>一条跑了 30 小时、刚刚结束的运行：闲置从结束那一刻算起，不从它开始布置的那一刻算起。</summary>
    [Fact]
    public async Task ALongRunThatJustEnded_IsNotIdle()
    {
        var preparer = Preparer();
        var workspace = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        var nodeModules = Path.Combine(workspace.WorkDirectory, "node_modules");
        Directory.CreateDirectory(nodeModules);
        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromHours(30));

        await preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);
        Directory.SetLastWriteTimeUtc(workspace.RootDirectory, DateTime.UtcNow.AddHours(-30));

        Gc().Collect(Options(), NoActiveRuns);

        Directory.Exists(nodeModules).ShouldBeTrue();
    }

    /// <summary>运行还没到终态（这里跑了很久）：不论闲置多久，它的目录一个字节都不能动。</summary>
    [Fact]
    public async Task AWorkspaceOwnedByAnUnfinishedRun_IsNeverCollected()
    {
        var runId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var workspace = await Preparer().PrepareAsync(Host, Context(runId, threadId), CancellationToken.None);
        var nodeModules = Path.Combine(workspace.WorkDirectory, "node_modules");
        Directory.CreateDirectory(nodeModules);
        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromDays(10));

        Gc().Collect(Options(), new HashSet<Guid> { runId });

        Directory.Exists(nodeModules).ShouldBeTrue("a run still executing in this directory owns it");
        Directory.Exists(workspace.RootDirectory).ShouldBeTrue();
    }

    /// <summary>同一线程上排着（或正在执行）的下一轮也拥有这个目录：它马上就要在这里续接。</summary>
    [Fact]
    public async Task AWorkspaceWhoseThreadHasAnUnfinishedRun_IsNeverCollected()
    {
        var threadId = Guid.NewGuid();
        var preparer = Preparer();
        var workspace = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), threadId), CancellationToken.None);
        await preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);
        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromDays(10));

        Gc().Collect(Options(), new HashSet<Guid> { threadId });

        Directory.Exists(workspace.RootDirectory).ShouldBeTrue();
    }

    /// <summary>对照：终态且真的闲置了，照常整个回收。否则把回收器整个关掉也一样绿。</summary>
    [Fact]
    public async Task AFinishedAndIdleWorkspace_IsCollected()
    {
        var preparer = Preparer();
        var workspace = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);
        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromDays(10));

        Gc().Collect(Options(), new HashSet<Guid> { Guid.NewGuid() });

        Directory.Exists(workspace.RootDirectory).ShouldBeFalse();
    }

    /// <summary>
    /// 用户目录模式下一次硬崩溃（没有清理）：闲置到期后，运行根里的受管文件（含带回写凭据的 MCP 配置）被删掉，用户的目录一个字节不动。
    /// </summary>
    [Fact]
    public async Task ACrashedRunInAUserDirectory_LosesItsManagedFiles_ButTheUsersDirectoryIsUntouched()
    {
        var userRepo = TestScratch.CreateDirectory("gc-user-repo");
        try
        {
            File.WriteAllText(Path.Combine(userRepo, "source.txt"), "precious");
            var workspace = await Preparer().PrepareAsync(Host, new CliRunContext
            {
                RunId = Guid.NewGuid(),
                AgentId = Guid.NewGuid(),
                Provider = CliBuiltInProviders.All["claude"],
                StableBrief = "# Agent",
                WorkDirectoryMode = CliWorkDirectoryMode.UserProvided,
                UserWorkDirectory = userRepo,
                McpConfigJson = """{"mcpServers":{}}"""
            }, CancellationToken.None);
            File.Delete(Path.Combine(workspace.RootDirectory, CliWorkspaceLayout.RunMarkerFileName)); // 占用者早已结束
            WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromDays(10));

            Gc().Collect(Options(), NoActiveRuns);

            File.Exists(workspace.McpConfigPath!).ShouldBeFalse();
            Directory.Exists(Path.Combine(workspace.RootDirectory, CliWorkspaceLayout.MetadataDirectoryName)).ShouldBeFalse();
            File.ReadAllText(Path.Combine(userRepo, "source.txt")).ShouldBe("precious");
            Directory.EnumerateFileSystemEntries(userRepo).ShouldHaveSingleItem();
        }
        finally
        {
            TestScratch.Delete(userRepo);
        }
    }

    /// <summary>对照：终态、闲置超过产物期限但没到完全清理期限，只删可再生目录。</summary>
    [Fact]
    public async Task AFinishedWorkspacePastTheArtifactTtl_LosesOnlyRegenerableDirectories()
    {
        var preparer = Preparer();
        var workspace = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        var nodeModules = Path.Combine(workspace.WorkDirectory, "node_modules");
        Directory.CreateDirectory(nodeModules);
        File.WriteAllText(Path.Combine(workspace.WorkDirectory, "result.txt"), "the agent's work");
        await preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);
        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromHours(13));

        Gc().Collect(Options(), NoActiveRuns);

        Directory.Exists(nodeModules).ShouldBeFalse();
        File.Exists(Path.Combine(workspace.WorkDirectory, "result.txt")).ShouldBeTrue();
    }

    /// <summary>
    /// 配置的名单整体替换默认名单。.NET 配置绑定对预置了元素的 List 是追加语义：此前默认名单写在属性初值里，
    /// 部署方配一条想收窄，生效的是默认 8 条再加 1 条。
    /// </summary>
    [Fact]
    public void ConfiguredArtifactPatterns_ReplaceTheDefaults_InsteadOfBeingAppendedToThem()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Cli:Gc:ArtifactPatterns:0"] = "node_modules" })
            .Build();
        var options = new CliAgentOptions();
        configuration.GetSection("AI:Cli").Bind(options);

        options.Gc.ResolveArtifactPatterns().ShouldBe(["node_modules"]);
        new CliAgentOptions().Gc.ResolveArtifactPatterns().ShouldBe(CliWorkspaceGcOptions.DefaultArtifactPatterns);
    }

    /// <summary>
    /// <c>bin/</c> 在很多仓库里是受版本控制的源码（Rails 的 binstubs、脚本仓库），默认名单不删它；
    /// <c>.git</c> 子树真的不进入：此前一次性枚举全部子目录，<c>.git/refs/heads/target/</c>（名为 <c>target/x</c> 的分支）照样命中被删。
    /// </summary>
    [Fact]
    public async Task ArtifactCleanup_KeepsBinAndNeverWalksIntoGit()
    {
        var preparer = Preparer();
        var workspace = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        var repo = Path.Combine(workspace.WorkDirectory, "app");
        var trackedBin = Path.Combine(repo, "bin");
        var branchRefs = Path.Combine(repo, ".git", "refs", "heads", "target");
        var nodeModules = Path.Combine(repo, "node_modules");
        Directory.CreateDirectory(trackedBin);
        File.WriteAllText(Path.Combine(trackedBin, "setup"), "#!/bin/sh");
        Directory.CreateDirectory(branchRefs);
        File.WriteAllText(Path.Combine(branchRefs, "x"), "0123456789abcdef");
        Directory.CreateDirectory(nodeModules);
        await preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);
        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromHours(13));

        Gc().Collect(Options(), NoActiveRuns);

        Directory.Exists(nodeModules).ShouldBeFalse("the control: regenerable directories are still collected");
        File.Exists(Path.Combine(trackedBin, "setup")).ShouldBeTrue();
        File.Exists(Path.Combine(branchRefs, "x")).ShouldBeTrue("nothing inside .git is ever matched");
    }

    /// <summary>
    /// 一次硬崩溃留在运行根目录的身份哨兵，同一线程的下一轮覆盖它之后，结束时要把它删掉。
    /// </summary>
    /// <remarks>
    /// 此前「已存在就不登记」：哨兵被同线程以后每一轮覆盖却永远不删，根目录再也没有条目增删，
    /// 它的修改时间就此冻结在崩溃那一轮。
    /// </remarks>
    [Fact]
    public async Task ALeftoverMarkerOfTheSameThread_IsRemovedWhenTheNextRunEnds()
    {
        var threadId = Guid.NewGuid();
        var preparer = Preparer();

        // 第一轮布置完就「崩溃」：没有清理
        var crashed = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), threadId), CancellationToken.None);
        var rootMarker = Path.Combine(crashed.RootDirectory, CliWorkspaceLayout.RunMarkerFileName);
        var workdirMarker = Path.Combine(crashed.WorkDirectory, CliWorkspaceLayout.MetadataDirectoryName, CliWorkspaceLayout.RunMarkerFileName);
        File.Exists(rootMarker).ShouldBeTrue();

        var next = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), threadId), CancellationToken.None);
        await preparer.CleanupAsync(Host, next, removeAll: false, CancellationToken.None);

        File.Exists(rootMarker).ShouldBeFalse();
        File.Exists(workdirMarker).ShouldBeFalse();
    }

    /// <summary>
    /// 每工作区配置目录里是 CLI 的会话记录：产物清理只进工作目录，碰不到它（线程还可能续接）；整个工作区被回收时它一起走。
    /// </summary>
    [Fact]
    public async Task APerWorkspaceConfigDirectory_SurvivesTheArtifactSweep_AndGoesWithTheWorkspace()
    {
        var preparer = Preparer();
        var workspace = await preparer.PrepareAsync(Host, Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        await preparer.CleanupAsync(Host, workspace, removeAll: false, CancellationToken.None);
        var transcript = WriteTranscript(workspace.RootDirectory);
        var nodeModules = Path.Combine(workspace.WorkDirectory, "node_modules");
        Directory.CreateDirectory(nodeModules);

        // 产物期限已过、完全清理的期限未到；名单里刻意放一个与会话目录同名的条目
        var options = Options();
        options.Gc.ArtifactPatterns = ["node_modules", "projects"];
        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromHours(13));
        WorkspaceAging.Gc(options).Collect(options, NoActiveRuns);

        Directory.Exists(nodeModules).ShouldBeFalse();
        File.Exists(transcript).ShouldBeTrue("the next turn of the thread resumes from it");

        WorkspaceAging.Age(workspace.RootDirectory, TimeSpan.FromHours(25));
        WorkspaceAging.Gc(options).Collect(options, NoActiveRuns);

        File.Exists(transcript).ShouldBeFalse();
    }

    /// <summary>按 CLI 的形状在运行根的每工作区配置目录里放一份会话记录。</summary>
    internal static string WriteTranscript(string runRoot)
    {
        var configDirectory = CliWorkspaceLayout.ResolveWorkspaceConfigDirectory(runRoot, CliBuiltInProviders.All["claude"], CliHostPaths.Native);
        var projectDirectory = Path.Combine(configDirectory, "projects", "C--workspaces-host-thread-workdir");
        Directory.CreateDirectory(projectDirectory);
        var transcript = Path.Combine(projectDirectory, Guid.NewGuid() + ".jsonl");
        File.WriteAllText(transcript, """{"type":"tool_result","content":"file contents from the user's machine"}""");
        return transcript;
    }

    private CliWorkspaceGcService Gc() => WorkspaceAging.Gc(Options());
}

/// <summary>回收器向运行表要的「谁还没结束」：排队、已认领、执行中的运行与它们的线程，终态的不算。</summary>
public class WorkspaceGcRunStateTests : IntegratedTestBase<CliQueueDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliQueueDbContext, CliRun, Guid>>();
    }

    [Fact]
    public async Task ActiveOwners_AreTheUnfinishedRunsAndTheirThreads()
    {
        var queued = Seed(CliRunStatus.Queued, Guid.NewGuid());
        var dispatched = Seed(CliRunStatus.Dispatched, threadId: null);
        var running = Seed(CliRunStatus.Running, Guid.NewGuid());
        var completed = Seed(CliRunStatus.Completed, Guid.NewGuid());
        var failed = Seed(CliRunStatus.Failed, Guid.NewGuid());
        await DbContext.SaveChangesAsync();

        var gc = new CliWorkspaceGcService(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new TestOptionsMonitor<CliAgentOptions>(new CliAgentOptions { Enabled = true }),
            NullLogger<CliWorkspaceGcService>.Instance);

        var owners = await gc.LoadActiveOwnersAsync(CancellationToken.None);

        owners.ShouldBe(
            new[] { queued.Id, queued.ThreadId!.Value, dispatched.Id, running.Id, running.ThreadId!.Value },
            ignoreOrder: true);
        owners.ShouldNotContain(completed.Id);
        owners.ShouldNotContain(failed.ThreadId!.Value);
    }

    private CliRun Seed(CliRunStatus status, Guid? threadId)
    {
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            ThreadId = threadId,
            Status = status,
            Prompt = "turn"
        };
        DbContext.Set<CliRun>().Add(run);
        return run;
    }
}

/// <summary>回收测试的共用工具：把一个布置好的运行目录整体「放老」。</summary>
internal static class WorkspaceAging
{
    public static CliWorkspaceGcService Gc(CliAgentOptions options)
        => new(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new TestOptionsMonitor<CliAgentOptions>(options),
            NullLogger<CliWorkspaceGcService>.Instance);

    /// <summary>
    /// 目录时间、回收元数据里的两个时间、身份哨兵的修改时间全部拨回 <paramref name="age"/> 之前：
    /// 回收器取它们中最晚的那个，只拨一个等于没拨。
    /// </summary>
    public static void Age(string directory, TimeSpan age)
    {
        var past = DateTime.UtcNow - age;

        var metadataPath = Path.Combine(directory, CliWorkspaceLayout.GcMetadataFileName);
        if (File.Exists(metadataPath))
        {
            var metadata = JsonSerializer.Deserialize<CliWorkspaceGcMetadata>(File.ReadAllText(metadataPath))!;
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata with { CreatedAt = past, LastActiveAt = past }));
        }

        var markerPath = Path.Combine(directory, CliWorkspaceLayout.RunMarkerFileName);
        if (File.Exists(markerPath))
        {
            File.SetLastWriteTimeUtc(markerPath, past);
        }

        Directory.SetCreationTimeUtc(directory, past);
        Directory.SetLastWriteTimeUtc(directory, past);
    }
}
