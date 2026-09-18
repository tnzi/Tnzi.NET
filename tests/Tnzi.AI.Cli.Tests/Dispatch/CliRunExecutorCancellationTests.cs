using System.Runtime.CompilerServices;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 取消一条<b>正在跑</b>的运行必须让它停在 Cancelled，而不是被打回队列再跑一遍。
/// </summary>
/// <remarks>
/// <para>
/// 用户取消与宿主停机走的是同一枚令牌（登记处把运行令牌链到宿主的 stoppingToken 上），
/// 执行器只凭令牌分不清两者。此前的形态：进程被杀之后 <c>CompleteAsync</c> 仍带着已取消的令牌
/// 去写库，写库抛 OCE，掉进「宿主停机 → 打回队列」分支，整实体回写又把内存里过期的
/// <c>CancelRequested=false</c> 盖回数据库 —— 下一轮认领谓词 <c>Queued &amp;&amp; !CancelRequested</c>
/// 命中，同一条运行从头再跑一次、再付一次钱，而这一次已经没有人能取消它。
/// </para>
/// <para>
/// 这组用例跑的是真实的 <see cref="CliRunExecutor"/> + SQLite 仓储；只有进程与协议层是假的
/// （一个把「运行中」阻塞到令牌被取消为止的适配器）。
/// </para>
/// </remarks>
public class CliRunExecutorCancellationTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();
    }

    [Fact]
    public async Task Cancel_WhileRunning_EndsCancelled_KeepsCancelRequested_AndIsNotReclaimed()
    {
        var run = await SeedClaimedRunAsync();
        var adapter = new BlockingAdapter();
        var executor = CreateExecutor(adapter);
        using var cts = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(run.Id, cts.Token);
        await adapter.Started.Task.WaitAsync(TestTimeout);

        // 与 CliAgentDispatcher.CancelAsync 同序：先写库（跨副本的权威事实），再取消本地令牌。
        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));
        await cts.CancelAsync();

        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Cancelled);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        persisted.CancelRequested.ShouldBeTrue("the cancel flag is the user's decision; a full-entity write must not overwrite it");
        persisted.CompletedAt.ShouldNotBeNull();
        persisted.LeaseExpiresAt.ShouldBeNull();

        // 终态的运行不会再被任何副本认领。
        (await CreateProcessor().TryClaimAsync(CancellationToken.None)).ShouldBeNull();
        adapter.Runs.ShouldBe(1, "a cancelled run must never be executed a second time");
    }

    [Fact]
    public async Task HostShutdown_WhileRunning_ReleasesToQueue_WithoutTouchingCancelRequested()
    {
        var run = await SeedClaimedRunAsync();
        var adapter = new BlockingAdapter();
        var executor = CreateExecutor(adapter);
        using var hostStopping = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(run.Id, hostStopping.Token);
        await adapter.Started.Task.WaitAsync(TestTimeout);

        // 没有人写过 CancelRequested：令牌被取消只能是宿主在停机。
        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Queued);
        persisted.CancelRequested.ShouldBeFalse();
        persisted.ClaimedByHostId.ShouldBeNull();
        persisted.LeaseExpiresAt.ShouldBeNull();
        persisted.CompletedAt.ShouldBeNull();

        // 打回队列的运行必须还能被认领：这是「停机不丢任务」的另一半。
        (await CreateProcessor().TryClaimAsync(CancellationToken.None)).ShouldBe(run.Id);
    }

    [Fact]
    public async Task Cancel_BeforeProcessStarts_EndsCancelled_NotQueued()
    {
        // 令牌在进程起来之前就被取消（取消恰好落在布置工作区的那几毫秒里）：
        // 这条路径上 RunProcessAsync 根本没跑到，终态必须由 ExecuteAsync 的取消分支自己写。
        var run = await SeedClaimedRunAsync();
        var adapter = new BlockingAdapter();
        using var cts = new CancellationTokenSource();
        var executor = CreateExecutor(adapter, beforeProcessStart: () => cts.Cancel());

        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));

        await executor.ExecuteAsync(run.Id, cts.Token).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Cancelled);
        persisted.CancelRequested.ShouldBeTrue();
        persisted.CompletedAt.ShouldNotBeNull();
        adapter.Runs.ShouldBe(0);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<CliRun> SeedClaimedRunAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = false
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/nonexistent/claude",
            Status = CliRuntimeStatus.Online
        });

        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "do the thing"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        // 执行器自己按 Id 加载；把种子实体从跟踪器里摘掉，免得两份同 Id 的实例撞在一起。
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private CliRunQueueProcessor CreateProcessor()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            NullLogger<CliRunQueueProcessor>.Instance);

    private CliRunExecutor CreateExecutor(BlockingAdapter adapter, Action? beforeProcessStart = null)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var descriptor = new CliProviderDescriptor
        {
            Key = "claude",
            DisplayName = "Claude Code",
            Protocol = CliAgentProtocol.StreamJson,
            DefaultExecutable = "claude"
        };

        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(descriptor);

        var adapters = new Mock<ICliProtocolAdapterFactory>();
        adapters.Setup(f => f.IsImplemented(CliAgentProtocol.StreamJson)).Returns(true);
        adapters.Setup(f => f.Create(CliAgentProtocol.StreamJson)).Returns(adapter);

        var processHost = new Mock<ICliProcessHost>();
        processHost.Setup(h => h.Name).Returns("fake");
        processHost
            .Setup(h => h.StartAsync(It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CliProcessSpec _, CancellationToken ct) =>
            {
                beforeProcessStart?.Invoke();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<ICliProcess>(new FakeProcess());
            });

        var workspaceRoot = Path.Combine(Path.GetTempPath(), "tnzi-cli-cancel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var workspace = new CliWorkspace
        {
            RootDirectory = workspaceRoot,
            WorkDirectory = workspaceRoot,
            OutputDirectory = workspaceRoot,
            LogDirectory = workspaceRoot
        };
        var preparer = new Mock<ICliWorkspacePreparer>();
        preparer
            .Setup(p => p.PrepareAsync(It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspace);
        preparer
            .Setup(p => p.ReuseAsync(It.IsAny<string>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CliWorkspace?)null);

        var executables = new Mock<ICliExecutableResolver>();
        executables.Setup(r => r.Resolve(descriptor)).Returns("/nonexistent/claude");

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents
            .Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "agent" });

        return new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            registry.Object,
            adapters.Object,
            processHost.Object,
            preparer.Object,
            Mock.Of<ICliBriefComposer>(),
            Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            executables.Object,
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            EnabledOptions(),
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance);
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    /// <summary>发一条事件后一直「运行中」，直到令牌被取消。</summary>
    private sealed class BlockingAdapter : ICliProtocolAdapter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Runs { get; private set; }

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Runs++;
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "working", SessionId = "sess-1" };
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => outcome.Cancelled
                ? new CliAgentResult
                {
                    Status = CliRunStatus.Cancelled,
                    FailureReason = CliRunFailureReason.Cancelled,
                    Error = "Execution cancelled.",
                    SessionId = "sess-1",
                    DurationMs = (long)outcome.Elapsed.TotalMilliseconds
                }
                : new CliAgentResult
                {
                    Status = CliRunStatus.Completed,
                    SessionId = "sess-1",
                    DurationMs = (long)outcome.Elapsed.TotalMilliseconds
                };
    }

    private sealed class FakeProcess : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = new FakeCliAgentTransport([]);

        public int ProcessId => 4242;

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 137 : null;

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            HasExited = true;
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(137);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
