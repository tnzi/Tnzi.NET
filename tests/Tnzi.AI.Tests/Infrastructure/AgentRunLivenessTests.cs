using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 执行运行的进程没了（重新部署、崩溃）之后，行不能永远停在 Pending / Running：
/// spawn 的并发上限按「仍在跑」计数，孤儿行会让这个用户此后的每一次 spawn 都被拒绝。
/// 在跑的运行定期刷新心跳，心跳停了超过 <see cref="AgentRunRecoveryService.StaleAfter"/> 的行被标为 Failed。
/// 真实 SQLite + EFCoreRepository，让条件更新经 EF Core 翻译。
/// </summary>
public class AgentRunLivenessTests : IDisposable
{
    // 临时文件库而不是内存库：心跳在后台按自己的作用域另开上下文写库，内存库只对单条连接可见。
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"agent-run-liveness-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<AgentRunFkDbContext> _options;
    private readonly ICurrentUser _currentUser;
    private readonly AgentRunFkDbContext _context;
    private readonly EFCoreRepository<AgentRunFkDbContext, AgentRun, Guid> _runs;
    private readonly RunStore _store;
    private readonly Guid _owner = Guid.NewGuid();

    public AgentRunLivenessTests()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns((Guid?)null);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);
        _currentUser = currentUser.Object;

        _options = new DbContextOptionsBuilder<AgentRunFkDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString())
            .Options;
        _context = new AgentRunFkDbContext(_options, _currentUser);
        _context.Database.EnsureCreated();
        _runs = new EFCoreRepository<AgentRunFkDbContext, AgentRun, Guid>(_context);
        _store = new RunStore(_runs, new EFCoreRepository<AgentRunFkDbContext, AgentRunNode, Guid>(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
        File.Delete(_databasePath);
        GC.SuppressFinalize(this);
    }

    private async Task<AgentRun> SeedAsync(AgentRunStatus status, TimeSpan heartbeatAge, string? workflowExecutionId = null)
    {
        var run = new AgentRun
        {
            CreatorId = _owner,
            Status = status,
            InputSummary = "seed",
            LastHeartbeatAt = DateTime.UtcNow - heartbeatAge,
            WorkflowExecutionId = workflowExecutionId
        };
        _context.Set<AgentRun>().Add(run);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return run;
    }

    private async Task<AgentRun> ReloadAsync(Guid id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Set<AgentRun>().AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private AgentRunRecoveryService CreateRecovery() => new(_runs, NullLogger<AgentRunRecoveryService>.Instance);

    [Fact]
    public async Task Recovery_RunsWhoseHeartbeatStopped_AreFailedAndNoLongerCountTowardsTheSpawnCap()
    {
        var running = await SeedAsync(AgentRunStatus.Running, TimeSpan.FromMinutes(30));
        var pending = await SeedAsync(AgentRunStatus.Pending, TimeSpan.FromMinutes(30));
        (await _store.CountActiveRootRunsByOwnerAsync(_owner)).ShouldBe(2);

        var recovered = await CreateRecovery().RecoverOrphanedRunsAsync();

        recovered.ShouldBe(2);
        foreach (var id in new[] { running.Id, pending.Id })
        {
            var row = await ReloadAsync(id);
            row.Status.ShouldBe(AgentRunStatus.Failed);
            row.Error.ShouldBe(AgentRunRecoveryService.OrphanedRunError);
        }
        (await _store.CountActiveRootRunsByOwnerAsync(_owner)).ShouldBe(0);
    }

    [Fact]
    public async Task Recovery_LeavesLiveFinishedAndWorkflowRunsAlone()
    {
        // 心跳新鲜 = 另一个节点上正常在跑；已收尾的不动；工作流运行由工作流自己的看门狗判定
        var live = await SeedAsync(AgentRunStatus.Running, TimeSpan.FromSeconds(30));
        var finished = await SeedAsync(AgentRunStatus.Completed, TimeSpan.FromMinutes(30));
        var workflow = await SeedAsync(AgentRunStatus.Running, TimeSpan.FromMinutes(30), workflowExecutionId: "wf-exec-1");

        (await CreateRecovery().RecoverOrphanedRunsAsync()).ShouldBe(0);

        (await ReloadAsync(live.Id)).Status.ShouldBe(AgentRunStatus.Running);
        (await ReloadAsync(finished.Id)).Status.ShouldBe(AgentRunStatus.Completed);
        (await ReloadAsync(workflow.Id)).Status.ShouldBe(AgentRunStatus.Running);
    }

    [Fact]
    public async Task Heartbeat_RefreshesRunningRowsOnly()
    {
        var running = await SeedAsync(AgentRunStatus.Running, TimeSpan.FromMinutes(30));
        var finished = await SeedAsync(AgentRunStatus.Completed, TimeSpan.FromMinutes(30));
        var heartbeat = CreateHeartbeat(TimeSpan.FromMinutes(1));

        await heartbeat.BeatAsync(running.Id, CancellationToken.None);
        await heartbeat.BeatAsync(finished.Id, CancellationToken.None);

        (await ReloadAsync(running.Id)).LastHeartbeatAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow - TimeSpan.FromMinutes(1));
        (await ReloadAsync(finished.Id)).LastHeartbeatAt!.Value.ShouldBeLessThan(DateTime.UtcNow - TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task Heartbeat_Lease_KeepsALongRunFromBeingRecovered()
    {
        var running = await SeedAsync(AgentRunStatus.Running, TimeSpan.FromMinutes(30));
        var heartbeat = CreateHeartbeat(TimeSpan.FromMilliseconds(20));

        await using (heartbeat.Start(running.Id))
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while ((await ReloadAsync(running.Id)).LastHeartbeatAt < DateTime.UtcNow - TimeSpan.FromMinutes(1) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }

        (await CreateRecovery().RecoverOrphanedRunsAsync()).ShouldBe(0);
        (await ReloadAsync(running.Id)).Status.ShouldBe(AgentRunStatus.Running);
    }

    [Fact]
    public async Task Runtime_TrackedRun_HoldsAHeartbeatForTheWholeRun()
    {
        var heartbeat = new RecordingHeartbeat();
        var (runtime, run) = CreateRuntime(heartbeat);

        await runtime.RunAsync(TrackedRequest(run));

        heartbeat.Started.ShouldBe([run.Id]);
        heartbeat.Disposed.ShouldBe(1);
    }

    [Fact]
    public async Task Runtime_TrackedStreamingRun_HoldsAHeartbeatForTheWholeRun()
    {
        var heartbeat = new RecordingHeartbeat();
        var (runtime, run) = CreateRuntime(heartbeat);

        await foreach (var _ in runtime.RunStreamingAsync(TrackedRequest(run)))
        {
            heartbeat.Disposed.ShouldBe(0);
        }

        heartbeat.Started.ShouldBe([run.Id]);
        heartbeat.Disposed.ShouldBe(1);
    }

    [Fact]
    public async Task Runtime_UntrackedRun_StartsNoHeartbeat()
    {
        var heartbeat = new RecordingHeartbeat();
        var (runtime, run) = CreateRuntime(heartbeat);

        await runtime.RunAsync(new AgentRunRequest { AgentId = run.AgentId, UserMessage = "hi" });

        heartbeat.Started.ShouldBeEmpty();
    }

    private AgentRunHeartbeat CreateHeartbeat(TimeSpan interval)
    {
        // 与生产一致：每次心跳一个作用域、一个上下文。共用测试的上下文会让后台心跳与测试的重读并发跑在同一个实例上，
        // EF 的并发检测随即抛出「A second operation was started on this context instance」。
        var services = new ServiceCollection()
            .AddScoped(_ => new AgentRunFkDbContext(_options, _currentUser))
            .AddScoped<IRepository<AgentRun, Guid>>(sp => new EFCoreRepository<AgentRunFkDbContext, AgentRun, Guid>(sp.GetRequiredService<AgentRunFkDbContext>()))
            .BuildServiceProvider();
        return new AgentRunHeartbeat(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentRunHeartbeat>.Instance, interval);
    }

    private static AgentRunRequest TrackedRequest(AgentRun run) => new()
    {
        AgentId = run.AgentId,
        UserMessage = "hi",
        EnableRunTracking = true,
        ExistingRunId = run.Id
    };

    private static (AgentRuntime Runtime, AgentRun Run) CreateRuntime(IAgentRunHeartbeat heartbeat)
    {
        var agentId = Guid.NewGuid();
        var run = new AgentRun { Id = Guid.NewGuid(), AgentId = agentId, Status = AgentRunStatus.Pending, Nodes = [] };

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        chatClient.Setup(c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() => new[] { new ChatResponseUpdate(ChatRole.Assistant, "done") { FinishReason = ChatFinishReason.Stop } }.ToAsyncEnumerable());
        var executor = new AgentExecutor(chatClient.Object, new AgentExecutorOptions { Name = "probe" });

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(agentId, null, null, null, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(AgentResolution.Success(executor, "test", "gpt-5", agentId));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

        var runtime = new AgentRuntime(
            resolver.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>()),
            Mock.Of<IWorkflowDelegator>(),
            new AgentExecutionContextAccessor(),
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<IOptionsMonitor<AIOptions>>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<AgentRuntime>>(),
            runHeartbeat: heartbeat);
        return (runtime, run);
    }

    private sealed class RecordingHeartbeat : IAgentRunHeartbeat
    {
        public List<Guid> Started { get; } = [];
        public int Disposed { get; private set; }

        public IAsyncDisposable Start(Guid runId)
        {
            Started.Add(runId);
            return new Lease(this);
        }

        private sealed class Lease(RecordingHeartbeat owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
