using Microsoft.Data.Sqlite;
using Tnzi.AI.Controllers.Admin;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// 管理端 <c>POST admin/agent-runs/{id}/cancel</c> 必须真的让运行停下来，而且停下来之后的收尾不能把 Cancelled 写回去。
/// </summary>
/// <remarks>
/// 此前控制器只调 <see cref="IAgentRunService.CancelAsync"/>：行翻成 Cancelled，后台任务的取消令牌从未触发，
/// 模型继续调工具、耗 token，跑完由 <c>RunTracker</c> 用后台作用域里那份实体把状态写回 Completed。
/// </remarks>
public class AgentRunAdminCancelTests
{
    [Fact]
    public async Task Cancel_TripsTheBackgroundRunsCancellationToken()
    {
        var runId = Guid.NewGuid();
        var harness = new CancelHarness(new AgentRun { Id = runId, Status = AgentRunStatus.Running, Nodes = [] });
        using var cts = new CancellationTokenSource();
        harness.Registry.Register(runId, cts);

        var result = await harness.Controller.Cancel(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
        cts.IsCancellationRequested.ShouldBeTrue("the admin cancel must stop the in-process run, not only flip its row");
        harness.Registry.GetCancellation(runId).ShouldNotBeNull().Kind.ShouldBe(RunCancellationKind.Killed);
        harness.RunService.Verify(x => x.CancelAsync(runId), Times.Once);
    }

    [Fact]
    public async Task Cancel_WorkflowRun_CancelsTheWorkflowExecution()
    {
        var runId = Guid.NewGuid();
        const string executionId = "wf-exec-1";
        var harness = new CancelHarness(new AgentRun
        {
            Id = runId,
            Status = AgentRunStatus.Running,
            WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowExecutionId = executionId,
            Nodes = []
        });
        harness.WorkflowControl.Setup(x => x.CancelAsync(executionId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await harness.Controller.Cancel(runId);

        result.Succeeded.ShouldBeTrue(result.Message);
        harness.WorkflowControl.Verify(x => x.CancelAsync(executionId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_UnknownRun_Returns404WithoutTouchingAnything()
    {
        var harness = new CancelHarness(run: null);

        var result = await harness.Controller.Cancel(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        harness.RunService.Verify(x => x.CancelAsync(It.IsAny<Guid>()), Times.Never);
    }

    private sealed class CancelHarness
    {
        public Mock<IAgentRunService> RunService { get; } = new();
        public Mock<IWorkflowExecutionControlService> WorkflowControl { get; } = new();
        public SubAgentRunCancellationRegistry Registry { get; } = new();
        public DefaultAgentRunAdminController Controller { get; }

        public CancelHarness(AgentRun? run)
        {
            var runStore = new Mock<IRunStore>();
            if (run != null)
            {
                runStore.Setup(x => x.GetWithNodesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
                RunService.Setup(x => x.CancelAsync(run.Id)).ReturnsAsync(() =>
                {
                    run.Status = AgentRunStatus.Cancelled;
                    return Result.Success();
                });
            }

            var dispatcher = new AgentRunSignalDispatcher(
                runStore.Object,
                RunService.Object,
                WorkflowControl.Object,
                Mock.Of<IWorkflowExecutionQueryService>(),
                Registry);

            var serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
            var control = new AgentRuntimeControlService(
                Mock.Of<ISubAgentExecutionService>(),
                dispatcher,
                runStore.Object,
                Mock.Of<IWorkflowService>(),
                Mock.Of<ISubAgentRegistry>(),
                serviceProvider);

            Controller = new DefaultAgentRunAdminController(RunService.Object, Mock.Of<IAgentTraceService>(), control);
        }
    }
}

/// <summary>
/// 取消与收尾赛跑：别的作用域已把行写成 Cancelled 时，<see cref="RunTracker"/> 的完成 / 失败收尾不得覆盖它。
/// 真实 SQLite + EFCoreRepository，两个 DbContext 各自跟踪同一行，复现后台运行与管理端请求各在自己作用域里的形态。
/// </summary>
public class RunTrackerCancelledRaceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ICurrentUser _currentUser;

    public RunTrackerCancelledRaceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns((Guid?)null);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);
        _currentUser = currentUserMock.Object;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AgentRunFkDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AgentRunFkDbContext>().UseSqlite(_connection).Options, _currentUser);

    private static RunTracker CreateTracker(AgentRunFkDbContext context)
        => new(
            new RunStore(
                new EFCoreRepository<AgentRunFkDbContext, AgentRun, Guid>(context),
                new EFCoreRepository<AgentRunFkDbContext, AgentRunNode, Guid>(context)),
            Mock.Of<ITraceStore>(),
            Mock.Of<ILogger<RunTracker>>());

    private async Task<Guid> SeedRunningAsync()
    {
        await using var context = CreateContext();
        var run = new AgentRun { Status = AgentRunStatus.Running, InputSummary = "seed" };
        context.Set<AgentRun>().Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    /// <summary>后台运行的作用域：已跟踪那一行（状态 Running），之后另一个作用域把它取消。</summary>
    private async Task<(AgentRunFkDbContext Context, AgentRun Run)> LoadInBackgroundScopeThenCancelElsewhereAsync(Guid runId, bool cancel)
    {
        var background = CreateContext();
        var run = await background.Set<AgentRun>().SingleAsync(r => r.Id == runId);
        run.Status.ShouldBe(AgentRunStatus.Running);

        if (cancel)
        {
            await using var admin = CreateContext();
            var row = await admin.Set<AgentRun>().SingleAsync(r => r.Id == runId);
            row.Status = AgentRunStatus.Cancelled;
            await admin.SaveChangesAsync();
        }

        return (background, run);
    }

    private async Task<AgentRunStatus> ReadStatusAsync(Guid runId)
    {
        await using var context = CreateContext();
        return await context.Set<AgentRun>().Where(r => r.Id == runId).Select(r => r.Status).SingleAsync();
    }

    private static AgentRunResult Completed() => new() { Response = "done", Status = AgentRunStatus.Completed };

    [Fact]
    public async Task UpdateRunOnCompletion_RowCancelledElsewhere_StaysCancelled()
    {
        var runId = await SeedRunningAsync();
        var (context, run) = await LoadInBackgroundScopeThenCancelElsewhereAsync(runId, cancel: true);
        await using (context)
        {
            await CreateTracker(context).UpdateRunOnCompletionAsync(run, Completed(), 10, CancellationToken.None);
        }

        (await ReadStatusAsync(runId)).ShouldBe(AgentRunStatus.Cancelled);
        run.Status.ShouldBe(AgentRunStatus.Cancelled, "the runtime returns run.Status to its caller");
    }

    [Fact]
    public async Task UpdateRunOnCompletion_NotCancelled_WritesTheResultStatus()
    {
        var runId = await SeedRunningAsync();
        var (context, run) = await LoadInBackgroundScopeThenCancelElsewhereAsync(runId, cancel: false);
        await using (context)
        {
            await CreateTracker(context).UpdateRunOnCompletionAsync(run, Completed(), 10, CancellationToken.None);
        }

        (await ReadStatusAsync(runId)).ShouldBe(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task UpdateRunOnFailure_RowCancelledElsewhere_StaysCancelled()
    {
        var runId = await SeedRunningAsync();
        var (context, run) = await LoadInBackgroundScopeThenCancelElsewhereAsync(runId, cancel: true);
        await using (context)
        {
            await CreateTracker(context).UpdateRunOnFailureAsync(run, new InvalidOperationException("boom"), 10, CancellationToken.None);
        }

        (await ReadStatusAsync(runId)).ShouldBe(AgentRunStatus.Cancelled);
    }

    [Fact]
    public async Task FinalizeStreamingCompleted_RowCancelledElsewhere_StaysCancelled()
    {
        var runId = await SeedRunningAsync();
        var (context, run) = await LoadInBackgroundScopeThenCancelElsewhereAsync(runId, cancel: true);
        await using (context)
        {
            await CreateTracker(context).FinalizeStreamingCompletedAsync(run, Completed(), 1, 1, 10, CancellationToken.None);
        }

        (await ReadStatusAsync(runId)).ShouldBe(AgentRunStatus.Cancelled);
    }

    [Fact]
    public async Task FinalizeStreamingFailed_RowCancelledElsewhere_StaysCancelled()
    {
        var runId = await SeedRunningAsync();
        var (context, run) = await LoadInBackgroundScopeThenCancelElsewhereAsync(runId, cancel: true);
        await using (context)
        {
            await CreateTracker(context).FinalizeStreamingFailedAsync(run, null, 10, CancellationToken.None);
        }

        (await ReadStatusAsync(runId)).ShouldBe(AgentRunStatus.Cancelled);
    }
}
