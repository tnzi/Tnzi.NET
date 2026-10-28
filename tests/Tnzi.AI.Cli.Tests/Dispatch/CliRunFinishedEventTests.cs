using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Data;
using Tnzi.EFCore.Data;
using Tnzi.EventBus;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 一条运行到达终态时恰好发布一条 <see cref="CliRunFinishedEvent"/>，无论它从哪条路径结束；还没结束的运行不发。
/// </summary>
/// <remarks>
/// <para>
/// 消费方把任务交给服务端的外部 agent 之后就关掉页面，要在这一轮结束时给手机推一条通知。在这之前模块对终态一个事件都不发，
/// 唯一的办法是在每台宿主上轮询所有在途运行：要么滞后，要么压库。
/// </para>
/// <para>
/// 用的是真实的 <see cref="LocalEventBus"/> 与一个注册进容器的处理器，处理器在收到事件的那一刻回读运行记录：
/// 「发布在终态提交之后」就是「这里读到的已经是终态」。执行器、队列处理器、调度器都是真实的，只有进程与协议层是假的。
/// </para>
/// </remarks>
public class CliRunFinishedEventTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111e1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222e2");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    // 字段初始化先于基类构造函数执行，ConfigureServices 里就能用到它。
    private readonly FinishedEventLog _log = new();
    private readonly string _workspaces = TestScratch.NewPath("finished-event");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();
        FinishedEventLog.Register(services, _log);
    }

    public override void Dispose()
    {
        TestScratch.Delete(_workspaces);
        base.Dispose();
    }

    [Fact]
    public async Task Completed_PublishesOneEvent_CarryingTheRun_AfterTheRunIsRecordedFinished()
    {
        var threadId = Guid.NewGuid();
        var agentRunId = Guid.NewGuid();
        var run = await SeedRunAsync(CliRunStatus.Dispatched, threadId: threadId, agentRunId: agentRunId);

        await ExecuteAsync(run.Id, new ResultAdapter(new CliAgentResult
        {
            Status = CliRunStatus.Completed,
            Output = "the deliverable",
            SessionId = "sess-1",
            DurationMs = 1234
        }));

        var entry = _log.Single();
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.Completed, "a handler that reads the run back must see it finished");
        entry.Event.RunId.ShouldBe(run.Id);
        entry.Event.AgentId.ShouldBe(AgentId);
        entry.Event.ThreadId.ShouldBe(threadId);
        entry.Event.AgentRunId.ShouldBe(agentRunId);
        entry.Event.UserId.ShouldBe(TestHelper.DefaultTestUserId);
        entry.Event.Status.ShouldBe(CliRunStatus.Completed);
        entry.Event.FailureReason.ShouldBeNull();
        entry.Event.Error.ShouldBeNull();
        entry.Event.Output.ShouldBe("the deliverable");
        entry.Event.DurationMs.ShouldBe(1234);

        var stored = await ReloadAsync(run.Id);
        stored.Output.ShouldBe("the deliverable");
        stored.ProviderSessionId.ShouldBe("sess-1");
        stored.MessageCount.ShouldBe(1);
        stored.CompletedAt.ShouldNotBeNull();
        stored.LeaseExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task TimedOut_PublishesOneEvent_WithTheWatchdogReason()
    {
        var run = await SeedRunAsync(CliRunStatus.Dispatched);

        await ExecuteAsync(run.Id, new ResultAdapter(new CliAgentResult
        {
            Status = CliRunStatus.TimedOut,
            FailureReason = CliRunFailureReason.HardTimeout,
            Error = "External agent timed out (HardTimeout)."
        }));

        var entry = _log.Single();
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.TimedOut);
        entry.Event.Status.ShouldBe(CliRunStatus.TimedOut);
        entry.Event.FailureReason.ShouldBe(CliRunFailureReason.HardTimeout);
        entry.Event.Error.ShouldBe("External agent timed out (HardTimeout).");
        entry.Event.Output.ShouldBeNull();
    }

    /// <summary>认领之后、进程起来之前就失败（这里是绑定已经没了）：同样是终态，同样一条。</summary>
    [Fact]
    public async Task FailingBeforeTheProcessStarts_PublishesOneFailedEvent()
    {
        var run = await SeedRunAsync(CliRunStatus.Dispatched, seedBinding: false);

        await ExecuteAsync(run.Id, new ResultAdapter(new CliAgentResult { Status = CliRunStatus.Completed }));

        var entry = _log.Single();
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.Failed);
        entry.Event.Status.ShouldBe(CliRunStatus.Failed);
        entry.Event.FailureReason.ShouldBe(CliRunFailureReason.Unknown);
        entry.Event.Error.ShouldBe("The agent no longer has an external CLI binding.");
        entry.Event.DurationMs.ShouldBe(0);
    }

    [Fact]
    public async Task CancellingARunningRun_PublishesOneCancelledEvent_AndNothingWhileItRuns()
    {
        var run = await SeedRunAsync(CliRunStatus.Dispatched);
        var adapter = new BlockingAdapter();
        using var cts = new CancellationTokenSource();

        var execution = ExecuteAsync(run.Id, adapter, cts.Token);
        await adapter.Started.Task.WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Running);
        _log.Count.ShouldBe(0, "a run that is still going has not finished");

        // 与 CliAgentDispatcher.CancelAsync 同序：先写库，再取消本地令牌
        await MarkCancelRequestedAsync(run.Id);
        await cts.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var entry = _log.Single();
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.Cancelled);
        entry.Event.Status.ShouldBe(CliRunStatus.Cancelled);
        entry.Event.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        entry.Event.Error.ShouldBe("Execution cancelled.");
        (await ReloadAsync(run.Id)).CancelRequested.ShouldBeTrue();
    }

    /// <summary>宿主停机把运行打回队列：它没有结束，别的副本还会接着跑，不发。</summary>
    [Fact]
    public async Task HostShutdownWhileRunning_ReleasesToTheQueue_AndPublishesNothing()
    {
        var run = await SeedRunAsync(CliRunStatus.Dispatched);
        var adapter = new BlockingAdapter();
        using var hostStopping = new CancellationTokenSource();

        var execution = ExecuteAsync(run.Id, adapter, hostStopping.Token);
        await adapter.Started.Task.WaitAsync(TestTimeout);
        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Queued);
        _log.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CancellingAQueuedRun_PublishesOneCancelledEvent()
    {
        var run = await SeedRunAsync(CliRunStatus.Queued);

        using var scope = ServiceProvider.CreateScope();
        var cancelled = await CreateDispatcher(scope.ServiceProvider).CancelAsync(run.Id);

        cancelled.Succeeded.ShouldBeTrue(cancelled.Message);
        var entry = _log.Single();
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.Cancelled);
        entry.Event.Status.ShouldBe(CliRunStatus.Cancelled);
        entry.Event.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        entry.Event.Error.ShouldBeNull("nobody recorded a reason for cancelling a run that never started");
    }

    /// <summary>
    /// 用户取消之后执行它的宿主死掉了：没有人能在那边落终态，租约回收在另一台宿主上把它判成 Cancelled，事件由回收方发。
    /// 同一轮里租约过期但没人要求取消的那条只是打回队列，不发。
    /// </summary>
    [Fact]
    public async Task ARunLostWithItsHostAfterCancellation_IsFinishedByTheLeaseReclaim_WhichPublishesOnce()
    {
        var cancelledRun = await SeedRunAsync(CliRunStatus.Running, leaseExpiresAt: DateTime.UtcNow.AddMinutes(-5), cancelRequested: true);
        var abandonedRun = await SeedRunAsync(CliRunStatus.Running, leaseExpiresAt: DateTime.UtcNow.AddMinutes(-5), seedBinding: false);

        await CreateProcessor().ReclaimExpiredLeasesAsync(CancellationToken.None);

        var entry = _log.Single();
        entry.Event.RunId.ShouldBe(cancelledRun.Id);
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.Cancelled);
        entry.Event.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        entry.Event.Error.ShouldBe("Execution cancelled.");
        (await ReloadAsync(abandonedRun.Id)).Status.ShouldBe(CliRunStatus.Queued);

        // 下一轮回收看到的已是终态，不会再发
        await CreateProcessor().ReclaimExpiredLeasesAsync(CancellationToken.None);
        _log.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecutionThrowing_IsFinishedByTheQueueFallback_WhichPublishesOneFailedEvent()
    {
        var run = await SeedRunAsync(CliRunStatus.Queued);
        var processor = new ThrowingProcessor(ServiceProvider.GetRequiredService<IServiceScopeFactory>(), finishFirst: null);

        await RunOneClaimAsync(processor);

        var entry = _log.Single();
        entry.Event.RunId.ShouldBe(run.Id);
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.Failed);
        entry.Event.FailureReason.ShouldBe(CliRunFailureReason.Unknown);
        entry.Event.Error.ShouldBe("executor blew up");
    }

    /// <summary>
    /// 执行器常常是写完终态之后、收尾时才抛出的：兜底不能把已经写下（并已经通知过）的结局改判成 Failed，也不能再发一条。
    /// 此前兜底的条件是「不是 Completed」，一条已经 Cancelled 的运行会被改写成 Failed。
    /// </summary>
    [Fact]
    public async Task ExecutionThrowingAfterTheRunFinished_TheFallbackNeitherRewritesItNorPublishesAgain()
    {
        var run = await SeedRunAsync(CliRunStatus.Queued);
        var cancelled = new CliRunTermination(CliRunStatus.Cancelled, CliRunFailureReason.Cancelled, "Execution cancelled.");
        var processor = new ThrowingProcessor(ServiceProvider.GetRequiredService<IServiceScopeFactory>(), finishFirst: cancelled);

        await RunOneClaimAsync(processor);

        _log.Single().Event.Status.ShouldBe(CliRunStatus.Cancelled);
        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Cancelled);
        stored.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
    }

    /// <summary>
    /// 两个写入者为同一条运行赛跑（租约过期之后别处接手、或兜底撞上收尾）：先到者写下终态并发布，后到者什么都不写、什么都不发。
    /// </summary>
    [Fact]
    public async Task TwoWritersFinishingTheSameRun_OnlyTheFirstWritesAndPublishes()
    {
        var run = await SeedRunAsync(CliRunStatus.Running);

        using (var first = ServiceProvider.CreateScope())
        {
            (await CreateFinalizer(first.ServiceProvider).FinishAsync(
                run.Id, new CliRunTermination(CliRunStatus.Completed, null, null) { Output = "first" })).ShouldBeTrue();
        }

        using (var second = ServiceProvider.CreateScope())
        {
            (await CreateFinalizer(second.ServiceProvider).FinishAsync(
                run.Id, new CliRunTermination(CliRunStatus.Failed, CliRunFailureReason.Unknown, "second"))).ShouldBeFalse();
        }

        _log.Single().Event.Status.ShouldBe(CliRunStatus.Completed);
        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Completed);
        stored.Output.ShouldBe("first");
        stored.Error.ShouldBeNull();
    }

    /// <summary>
    /// 执行器跑着的时候，这条运行已经在别处结束了（这里模拟另一台宿主的兜底）：进程随后正常跑完，执行器的收尾输掉这场赛跑，
    /// 运行记录保留先到的结局，事件仍然只有一条。此前收尾是整实体回写，后到的结局会盖掉已经通知出去的那一个。
    /// </summary>
    [Fact]
    public async Task AnExecutorThatLosesTheRace_NeitherRewritesTheRunNorPublishes()
    {
        var run = await SeedRunAsync(CliRunStatus.Dispatched);
        var adapter = new GatedAdapter();

        var execution = ExecuteAsync(run.Id, adapter);
        await adapter.Started.Task.WaitAsync(TestTimeout);

        using (var elsewhere = ServiceProvider.CreateScope())
        {
            (await CreateFinalizer(elsewhere.ServiceProvider).FinishAsync(
                run.Id, new CliRunTermination(CliRunStatus.Failed, CliRunFailureReason.Unknown, "finished elsewhere"))).ShouldBeTrue();
        }

        adapter.Release();
        await execution.WaitAsync(TestTimeout);

        var entry = _log.Single();
        entry.Event.Status.ShouldBe(CliRunStatus.Failed);
        entry.Event.Error.ShouldBe("finished elsewhere");
        var stored = await ReloadAsync(run.Id);
        stored.Status.ShouldBe(CliRunStatus.Failed);
        stored.Output.ShouldBeNull();
        stored.Error.ShouldBe("finished elsewhere");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<CliRun> SeedRunAsync(
        CliRunStatus status,
        Guid? threadId = null,
        Guid? agentRunId = null,
        DateTime? leaseExpiresAt = null,
        bool cancelRequested = false,
        bool seedBinding = true)
    {
        if (seedBinding && !await DbContext.Set<CliAgentBinding>().AnyAsync())
        {
            DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
            {
                AgentId = AgentId,
                CliRuntimeId = RuntimeId,
                MaterializeSkills = false,
                InjectAgentInstructions = false
            });
        }

        if (!await DbContext.Set<CliRuntime>().AnyAsync())
        {
            DbContext.Set<CliRuntime>().Add(new CliRuntime
            {
                Id = RuntimeId,
                HostId = "TEST-HOST",
                ProviderKey = "claude",
                Name = "claude @ TEST-HOST",
                ExecutablePath = "/nonexistent/claude",
                Status = CliRuntimeStatus.Online
            });
        }

        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            AgentRunId = agentRunId,
            ThreadId = threadId,
            Status = status,
            ClaimedByHostId = status is CliRunStatus.Dispatched or CliRunStatus.Running ? "TEST-HOST" : null,
            DispatchedAt = status is CliRunStatus.Dispatched or CliRunStatus.Running ? DateTime.UtcNow : null,
            LeaseExpiresAt = leaseExpiresAt ?? (status is CliRunStatus.Dispatched or CliRunStatus.Running ? DateTime.UtcNow.AddMinutes(5) : null),
            CancelRequested = cancelRequested,
            Prompt = "do the thing"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private Task MarkCancelRequestedAsync(Guid runId)
        => DbContext.Set<CliRun>().Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    /// <summary>执行器在自己的作用域里跑，与生产一致（队列处理器每条运行开一个作用域）。</summary>
    private async Task ExecuteAsync(Guid runId, ICliProtocolAdapter adapter, CancellationToken cancellationToken = default)
    {
        using var scope = ServiceProvider.CreateScope();
        await CreateExecutor(scope.ServiceProvider, adapter).ExecuteAsync(runId, cancellationToken);
    }

    /// <summary>让处理器认领一条、交给（会抛出的）执行，等这条执行连同兜底一起收尾（槽归还）。</summary>
    private static async Task RunOneClaimAsync(CliRunQueueProcessor processor)
    {
        using var slots = new SemaphoreSlim(1, 1);
        (await processor.TryStartNextAsync(slots, CancellationToken.None)).ShouldBeTrue();

        var deadline = DateTime.UtcNow + TestTimeout;
        while (slots.CurrentCount == 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The claimed run never finished.");
            }

            await Task.Delay(20);
        }
    }

    private static CliRunFinalizer CreateFinalizer(IServiceProvider scoped)
        => new(
            scoped.GetRequiredService<IRepository<CliRun, Guid>>(),
            scoped.GetRequiredService<IEventBus>(),
            NullLogger.Instance);

    private CliRunQueueProcessor CreateProcessor()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(),
            NullLogger<CliRunQueueProcessor>.Instance);

    private static CliAgentDispatcher CreateDispatcher(IServiceProvider scoped)
        => new(
            scoped.GetRequiredService<IRepository<CliRun, Guid>>(),
            scoped.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            scoped.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            scoped.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(),
            scoped);

    /// <summary>租约很长：续租不在测试期间触发（SQLite 内存库只有一条连接，不让它与测试本身并发访问）。</summary>
    private static IOptionsMonitor<CliAgentOptions> Options()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true, LeaseDuration = TimeSpan.FromMinutes(30) });
        return monitor.Object;
    }

    private CliRunExecutor CreateExecutor(IServiceProvider scoped, ICliProtocolAdapter adapter)
    {
        var runs = scoped.GetRequiredService<IRepository<CliRun, Guid>>();

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

        var host = TestCliHost.Starting(() => new FakeProcess());

        var workspaceRoot = Path.Combine(_workspaces, Guid.NewGuid().ToString("N"));
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
            .Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspace);
        preparer
            .Setup(p => p.ReuseAsync(It.IsAny<ICliHostTarget>(), It.IsAny<string>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CliWorkspace?)null);

        return new CliRunExecutor(
            runs,
            scoped.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            scoped.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            scoped.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            TestAgents.Repository(AgentId),
            registry.Object,
            adapters.Object,
            host,
            preparer.Object,
            Mock.Of<ICliBriefComposer>(),
            Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            Options(),
            scoped.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            scoped.GetRequiredService<IServiceScopeFactory>(),
            eventBus: scoped.GetRequiredService<IEventBus>());
    }

    /// <summary>执行替身：可选地先把运行写成终态（模拟写完终态之后收尾时出错），然后抛出。</summary>
    private sealed class ThrowingProcessor(IServiceScopeFactory scopeFactory, CliRunTermination? finishFirst)
        : CliRunQueueProcessor(scopeFactory, new CliRunSignalHub(), new CliRunCancellationRegistry(), Options(), NullLogger<CliRunQueueProcessor>.Instance)
    {
        private readonly IServiceScopeFactory _scopes = scopeFactory;

        internal override async Task ExecuteRunAsync(Guid runId, CancellationToken cancellationToken)
        {
            if (finishFirst is not null)
            {
                using var scope = _scopes.CreateScope();
                (await CreateFinalizer(scope.ServiceProvider).FinishAsync(runId, finishFirst)).ShouldBeTrue();
            }

            throw new InvalidOperationException("executor blew up");
        }
    }

    /// <summary>发一条事件，然后按给定的结果结束。</summary>
    private sealed class ResultAdapter(CliAgentResult result) : ICliProtocolAdapter
    {
        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "working" };
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome) => result;
    }

    /// <summary>发一条事件后一直「运行中」，直到令牌被取消。</summary>
    private sealed class BlockingAdapter : ICliProtocolAdapter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "working" };
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => outcome.Cancelled
                ? new CliAgentResult { Status = CliRunStatus.Cancelled, FailureReason = CliRunFailureReason.Cancelled, Error = "Execution cancelled." }
                : new CliAgentResult { Status = CliRunStatus.Completed };
    }

    /// <summary>发一条事件后停住，放行之后正常完成。</summary>
    private sealed class GatedAdapter : ICliProtocolAdapter
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public void Release() => _gate.TrySetResult();

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "working" };
            Started.TrySetResult();
            await _gate.Task.WaitAsync(cancellationToken);
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => new() { Status = CliRunStatus.Completed, Output = "late answer", DurationMs = 10 };
    }

    private sealed class FakeProcess : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = new FakeCliAgentTransport([]);

        public int ProcessId => 4242;

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 0 : null;

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            HasExited = true;
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// 真正的赛跑：两台宿主都读到「还没结束」，然后各自去写。判定「这次转换是不是我完成的」必须在写入语句本身里，
/// 只靠写之前读一眼，后写的那一方会把先写的结局盖掉并再发一条事件。
/// </summary>
/// <remarks>
/// 一个命令拦截器在终态写入语句执行之前，用同一条连接先提交另一个写入者的终态：读与写之间的窗口被确定地撑开。
/// </remarks>
public class CliRunFinishedEventRaceTests : IntegratedTestBase<CliBudgetDbContext>
{
    private readonly FinishedEventLog _log = new();
    private readonly CompetingWriteInterceptor _competingWriter = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_competingWriter);

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        FinishedEventLog.Register(services, _log);
    }

    [Fact]
    public async Task TheWriterWhoseUpdateLandsSecond_WritesNothing_AndPublishesNothing()
    {
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = CliRunStatus.Running,
            Prompt = "x"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var table = DbContext.Model.FindEntityType(typeof(CliRun))!.GetTableName();
        _competingWriter.CompetingSql =
            $"UPDATE \"{table}\" SET \"Status\" = {(int)CliRunStatus.Failed}, \"Error\" = 'won elsewhere' " +
            $"WHERE \"Id\" = '{run.Id.ToString().ToUpperInvariant()}'";

        using var scope = ServiceProvider.CreateScope();
        var finalizer = new CliRunFinalizer(
            scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            scope.ServiceProvider.GetRequiredService<IEventBus>(),
            NullLogger.Instance);

        var finished = await finalizer.FinishAsync(
            run.Id, new CliRunTermination(CliRunStatus.Completed, null, null) { Output = "late" });

        _competingWriter.CompetingSql.ShouldBeNull("the competing write must have run before the finalizer's update");
        finished.ShouldBeFalse();
        _log.Count.ShouldBe(0, "the other writer recorded this transition; it is the one that publishes");

        var stored = await DbContext.Set<CliRun>().AsNoTracking().FirstAsync(r => r.Id == run.Id);
        stored.Status.ShouldBe(CliRunStatus.Failed);
        stored.Error.ShouldBe("won elsewhere");
        stored.Output.ShouldBeNull();
    }

    /// <summary>armed 时，在下一条 UPDATE 执行之前先在同一连接上执行一次竞争写入（只一次）。</summary>
    private sealed class CompetingWriteInterceptor : DbCommandInterceptor
    {
        public string? CompetingSql { get; set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (CompetingSql is { } sql && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                CompetingSql = null;
                await using var competing = command.Connection!.CreateCommand();
                competing.Transaction = command.Transaction;
                competing.CommandText = sql;
                await competing.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}

/// <summary>
/// 多租户开启时，在没有租户的作用域里落下的终态（租约回收、执行兜底）照样带着运行自己的租户发布，处理器跑在那个租户下。
/// </summary>
public class CliRunFinishedEventTenantTests : IntegratedTestBase<CliMultiTenantDbContext>
{
    private static readonly Guid TenantId = Guid.Parse("33333333-3333-3333-3333-3333333333e3");

    private readonly FinishedEventLog _log = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));

        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRuntime, Guid>>();
        FinishedEventLog.Register(services, _log);
    }

    [Fact]
    public async Task ATenantsRunFinishedByTheLeaseReclaim_IsPublishedUnderThatTenant()
    {
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = CliRunStatus.Running,
            Prompt = "tenant work",
            TenantId = TenantId,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ClaimedByHostId = "dead-host",
            CancelRequested = true
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var processor = new CliRunQueueProcessor(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Mock.Of<IOptionsMonitor<CliAgentOptions>>(m => m.CurrentValue == new CliAgentOptions { Enabled = true }),
            NullLogger<CliRunQueueProcessor>.Instance);
        await processor.ReclaimExpiredLeasesAsync(CancellationToken.None);

        var entry = _log.Single();
        entry.Event.RunId.ShouldBe(run.Id);
        entry.Event.TenantId.ShouldBe(TenantId);
        entry.TenantSeenByHandler.ShouldBe(TenantId, "handlers run in the run's tenant, not the tenantless scope that recorded the transition");
        entry.StatusSeenByHandler.ShouldBe(CliRunStatus.Cancelled);
    }
}

/// <summary>
/// 收到的结束事件，连同处理器在收到那一刻回读到的运行状态与所处的租户。
/// </summary>
internal sealed class FinishedEventLog
{
    private readonly List<Entry> _entries = [];

    public int Count
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>真实的本地总线 + 一个回读运行记录的处理器。</summary>
    public static void Register(IServiceCollection services, FinishedEventLog log)
    {
        services.AddSingleton(log);
        services.AddSingleton<IEventBus>(sp => new LocalEventBus(sp, NullLogger<LocalEventBus>.Instance));
        services.AddScoped<IEventHandler<CliRunFinishedEvent>, RecordingHandler>();
    }

    public Entry Single()
    {
        lock (_entries)
        {
            return _entries.ShouldHaveSingleItem("exactly one finish event per run");
        }
    }

    private void Add(Entry entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    public sealed record Entry(CliRunFinishedEvent Event, CliRunStatus? StatusSeenByHandler, Guid? TenantSeenByHandler);

    private sealed class RecordingHandler(IRepository<CliRun, Guid> runs, ICurrentTenant currentTenant, FinishedEventLog log)
        : IEventHandler<CliRunFinishedEvent>
    {
        public async Task HandleAsync(CliRunFinishedEvent finished, CancellationToken cancellationToken = default)
        {
            var status = await runs.AsQueryable()
                .IgnoreQueryFilters()
                .Where(r => r.Id == finished.RunId)
                .Select(r => (CliRunStatus?)r.Status)
                .FirstOrDefaultAsync(cancellationToken);

            log.Add(new Entry(finished, status, currentTenant.Id));
        }
    }
}
