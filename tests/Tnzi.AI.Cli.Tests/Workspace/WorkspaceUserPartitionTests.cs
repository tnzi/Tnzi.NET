namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 按用户分区的工作区布局，以及回收器对两种布局与专用配置目录的处理。
/// </summary>
public class WorkspaceUserPartitionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tnzi-cli-part-" + Guid.NewGuid().ToString("N"));

    public WorkspaceUserPartitionTests()
    {
        Directory.CreateDirectory(_root);
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

    private CliAgentOptions Options(bool partition) => new()
    {
        Enabled = true,
        WorkspacesRoot = _root,
        PartitionWorkspacesByUser = partition,
        Gc = { CompletedTtl = TimeSpan.FromHours(1), OrphanTtl = TimeSpan.FromHours(1) }
    };

    private FileSystemWorkspacePreparer Preparer(bool partition)
        => new(new TestOptionsMonitor<CliAgentOptions>(Options(partition)), NullLogger<FileSystemWorkspacePreparer>.Instance);

    private static CliRunContext Context(Guid threadId, Guid? userId, Guid? tenantId = null) => new()
    {
        RunId = Guid.NewGuid(),
        ThreadId = threadId,
        UserId = userId,
        TenantId = tenantId,
        AgentId = Guid.NewGuid(),
        Provider = CliBuiltInProviders.All["claude"],
        StableBrief = "# Agent",
        WorkDirectoryMode = CliWorkDirectoryMode.PerThread
    };

    [Fact]
    public async Task PartitionOff_KeepsTheTenantThreadLayout()
    {
        var threadId = Guid.NewGuid();

        var workspace = await Preparer(partition: false).PrepareAsync(Context(threadId, Guid.NewGuid()), CancellationToken.None);

        workspace.RootDirectory.ShouldBe(Path.Combine(_root, "host", threadId.ToString("N")));
    }

    [Fact]
    public async Task PartitionOn_PutsTheSignedInUsersDirectoryBetweenTenantAndThread()
    {
        var threadId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var workspace = await Preparer(partition: true).PrepareAsync(Context(threadId, userId, tenantId), CancellationToken.None);

        workspace.RootDirectory.ShouldBe(Path.Combine(_root, tenantId.ToString("N"), "u-" + userId.ToString("N"), threadId.ToString("N")));
        workspace.WorkDirectory.ShouldStartWith(workspace.RootDirectory);
    }

    [Fact]
    public async Task PartitionOn_RunsWithoutASignedInUserShareTheAnonymousPartition()
    {
        var threadId = Guid.NewGuid();

        var workspace = await Preparer(partition: true).PrepareAsync(Context(threadId, userId: null), CancellationToken.None);

        workspace.RootDirectory.ShouldBe(Path.Combine(_root, "host", CliWorkspaceLayout.AnonymousUserPartition, threadId.ToString("N")));
    }

    /// <summary>
    /// 两个用户即使撞上同一个线程 id 也各在各的目录 —— 分区的意义就在这里。
    /// </summary>
    [Fact]
    public async Task PartitionOn_DifferentUsersNeverShareADirectory()
    {
        var threadId = Guid.NewGuid();
        var preparer = Preparer(partition: true);

        var first = await preparer.PrepareAsync(Context(threadId, Guid.NewGuid()), CancellationToken.None);
        var second = await preparer.PrepareAsync(Context(threadId, Guid.NewGuid()), CancellationToken.None);

        second.WorkDirectory.ShouldNotBe(first.WorkDirectory);
    }

    /// <summary>
    /// 回收器按目录名识别分区层，所以开关怎么切，过期的运行都收得到；分区目录空了也一并删掉。
    /// </summary>
    [Fact]
    public async Task Gc_CollectsExpiredRunsInsideUserPartitionsAndLegacyLayoutAlike()
    {
        var partitioned = await Preparer(partition: true).PrepareAsync(Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        var legacy = await Preparer(partition: false).PrepareAsync(Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        Age(partitioned.RootDirectory);
        Age(legacy.RootDirectory);

        Gc(Options(partition: true)).Collect(Options(partition: true));

        Directory.Exists(partitioned.RootDirectory).ShouldBeFalse();
        Directory.Exists(legacy.RootDirectory).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(partitioned.RootDirectory)).ShouldBeFalse();
    }

    /// <summary>
    /// 分区目录没有回收元数据；若被当成运行目录，会被判成孤儿，连同其下仍在用的运行一起删掉。
    /// </summary>
    [Fact]
    public async Task Gc_NeverTreatsAUserPartitionAsAnOrphanedRun()
    {
        var live = await Preparer(partition: true).PrepareAsync(Context(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        var partition = Path.GetDirectoryName(live.RootDirectory)!;
        Directory.SetCreationTimeUtc(partition, DateTime.UtcNow.AddDays(-30));
        Directory.SetLastWriteTimeUtc(partition, DateTime.UtcNow.AddDays(-30));

        Gc(Options(partition: true)).Collect(Options(partition: true));

        Directory.Exists(live.WorkDirectory).ShouldBeTrue();
    }

    /// <summary>
    /// 专用配置目录里是登录态与会话存档：它没有回收元数据，按运行目录的规矩会被当孤儿删掉。
    /// </summary>
    [Fact]
    public void Gc_LeavesTheIsolatedConfigDirectoryAlone()
    {
        var configDirectory = CliWorkspaceLayout.ResolveConfigDirectory(CliBuiltInProviders.All["claude"], Options(partition: false));
        Directory.CreateDirectory(Path.Combine(configDirectory, "projects"));
        File.WriteAllText(Path.Combine(configDirectory, ".credentials.json"), "{}");
        Age(Path.Combine(_root, CliWorkspaceLayout.ConfigDirectoryName));
        Age(configDirectory);

        Gc(Options(partition: false)).Collect(Options(partition: false));

        File.Exists(Path.Combine(configDirectory, ".credentials.json")).ShouldBeTrue();
    }

    private static CliWorkspaceGcService Gc(CliAgentOptions options)
        => new(new TestOptionsMonitor<CliAgentOptions>(options), NullLogger<CliWorkspaceGcService>.Instance);

    private static void Age(string directory)
    {
        var past = DateTime.UtcNow.AddDays(-10);
        Directory.SetCreationTimeUtc(directory, past);
        Directory.SetLastWriteTimeUtc(directory, past);
    }
}
