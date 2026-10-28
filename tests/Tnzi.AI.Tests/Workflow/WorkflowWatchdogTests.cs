using Tnzi.Modules;
using Tnzi.AI.Workflow.Options;

namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Watchdog 的扫描语义与它的调度者。
/// </summary>
/// <remarks>
/// ★ <c>ScanAsync</c> 此前在全仓<b>零调用方</b>：模块只把 <c>WorkflowWatchdogService</c>
/// 注册成 Scoped 服务，文档把"由宿主接调度"写成一个取舍，却既没有接线示例也没有启动提示。
/// 而 <c>Enabled</c> 默认 true，看上去它一直在工作 —— 实际结果是崩溃中断的执行实例永远停在
/// <c>Running</c>，<c>AwaitingApproval</c> 永不过期。
/// </remarks>
public class WorkflowWatchdogTests
{
    private static WorkflowExecution StaleRunning(DateTime updatedAt) => new()
    {
        ExecutionId = Guid.NewGuid().ToString("N"),
        Status = WorkflowExecutionStatus.Running,
        UpdatedTime = updatedAt
    };

    private static (WorkflowWatchdogService Service, Mock<IRepository<WorkflowExecution, Guid>> Repo) CreateService(
        List<WorkflowExecution> rows, WorkflowWatchdogOptions? options = null)
    {
        var repo = new Mock<IRepository<WorkflowExecution, Guid>>();
        repo.Setup(r => r.AsQueryable()).Returns(rows.BuildMock());

        var service = new WorkflowWatchdogService(
            repo.Object,
            NullLogger<WorkflowWatchdogService>.Instance,
            new StaticOptionsMonitor<WorkflowWatchdogOptions>(options ?? new WorkflowWatchdogOptions()));

        return (service, repo);
    }

    [Fact]
    public async Task ScanAsync_TakesTheBatchLimitInTheQuery_NotInMemory()
    {
        // ★ 此前是先 ToListAsync 拉回全部超时行、再在内存里 Take：MaxBatchSize 限制的是
        // "处理多少条"而不是"拉回多少条"，积压一多，每次扫描都要把整个积压读进内存 ——
        // 而这正是它本该防住的情形。
        var old = DateTime.UtcNow.AddHours(-5);
        var rows = Enumerable.Range(0, 20).Select(i => StaleRunning(old.AddMinutes(i))).ToList();

        var (service, repo) = CreateService(rows, new WorkflowWatchdogOptions { MaxBatchSize = 3 });

        var marked = await service.ScanAsync();

        marked.ShouldBe(3);
        repo.Verify(
            r => r.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ScanAsync_ProcessesTheOldestFirst()
    {
        var oldest = StaleRunning(DateTime.UtcNow.AddHours(-9));
        var newer = StaleRunning(DateTime.UtcNow.AddHours(-2));

        var (service, repo) = CreateService([newer, oldest], new WorkflowWatchdogOptions { MaxBatchSize = 1 });

        await service.ScanAsync();

        repo.Verify(
            r => r.UpdateAsync(It.Is<WorkflowExecution>(e => e.ExecutionId == oldest.ExecutionId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_FreshExecution_IsNotTimedOut()
    {
        var (service, repo) = CreateService([StaleRunning(DateTime.UtcNow)]);

        (await service.ScanAsync()).ShouldBe(0);
        repo.Verify(r => r.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // 关联的 AgentRun 投影
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkflowExecutionStatus.AwaitingApproval, AgentRunStatus.AwaitingApproval, "approval")]
    [InlineData(WorkflowExecutionStatus.AwaitingInput, AgentRunStatus.RequiresClarification, "input")]
    public async Task ScanAsync_TimedOutWait_FailsTheWaitingAgentRun(WorkflowExecutionStatus waiting, AgentRunStatus runStatus, string waitedFor)
    {
        // 等待中的执行没有引擎在跑：此前只有执行行被标成 Failed，AgentRun 永远停在等待，
        // agent-runs 列表照样给出「批准」，批准时才在工作流服务里 400。
        var execution = new WorkflowExecution
        {
            ExecutionId = Guid.NewGuid().ToString("N"),
            Status = waiting,
            UpdatedTime = DateTime.UtcNow.AddDays(-30)
        };
        var agentRun = new AgentRun { Id = Guid.NewGuid(), WorkflowExecutionId = execution.ExecutionId, Status = runStatus };
        var runs = RunRepositoryHolding(agentRun);

        var service = CreateServiceWithRuns([execution], runs.Object);
        (await service.ScanAsync()).ShouldBe(1);

        runs.Verify(r => r.UpdateAsync(
            It.Is<AgentRun>(a => a.Id == agentRun.Id && a.Status == AgentRunStatus.Failed && a.Error!.Contains(waitedFor)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanAsync_TimedOutRunningExecution_LeavesItsAgentRunToTheEngine()
    {
        // 超时的 Running 执行可能还有引擎在跑，它收场时自己写运行行。
        var execution = StaleRunning(DateTime.UtcNow.AddHours(-5));
        var runs = RunRepositoryHolding(new AgentRun { Id = Guid.NewGuid(), WorkflowExecutionId = execution.ExecutionId, Status = AgentRunStatus.Running });

        var service = CreateServiceWithRuns([execution], runs.Object);
        (await service.ScanAsync()).ShouldBe(1);

        runs.Verify(r => r.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanAsync_AgentRunUpdateFails_StillCountsTheExecutionAsMarked()
    {
        var execution = new WorkflowExecution
        {
            ExecutionId = Guid.NewGuid().ToString("N"),
            Status = WorkflowExecutionStatus.AwaitingApproval,
            UpdatedTime = DateTime.UtcNow.AddDays(-30)
        };
        var runs = new Mock<IRepository<AgentRun, Guid>>();
        runs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var service = CreateServiceWithRuns([execution], runs.Object);

        (await service.ScanAsync()).ShouldBe(1);
    }

    private static Mock<IRepository<AgentRun, Guid>> RunRepositoryHolding(AgentRun agentRun)
    {
        var runs = new Mock<IRepository<AgentRun, Guid>>();
        runs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentRun, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<AgentRun, bool>> predicate, CancellationToken _) =>
                predicate.Compile()(agentRun) ? agentRun : null);
        return runs;
    }

    private static WorkflowWatchdogService CreateServiceWithRuns(List<WorkflowExecution> rows, IRepository<AgentRun, Guid> runs)
    {
        var repo = new Mock<IRepository<WorkflowExecution, Guid>>();
        repo.Setup(r => r.AsQueryable()).Returns(rows.BuildMock());

        return new WorkflowWatchdogService(
            repo.Object,
            NullLogger<WorkflowWatchdogService>.Instance,
            new StaticOptionsMonitor<WorkflowWatchdogOptions>(new WorkflowWatchdogOptions()),
            runRepository: runs);
    }

    [Fact]
    public async Task ScanAsync_Disabled_DoesNothing()
    {
        var (service, repo) = CreateService(
            [StaleRunning(DateTime.UtcNow.AddHours(-9))],
            new WorkflowWatchdogOptions { Enabled = false });

        (await service.ScanAsync()).ShouldBe(0);
        repo.Verify(r => r.AsQueryable(), Times.Never);
    }

    // -------------------------------------------------------------------------
    // 调度者
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Module_RegistersTheWatchdogScheduler()
    {
        // 少了这条注册，Watchdog 就是一个谁都不调用的服务 —— 而它的开关默认是 true。
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        await new AIWorkflowModule()
            .ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));

        services.Any(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(WorkflowWatchdogHostedService)).ShouldBeTrue();
    }

    // ★下面几条都等「条件成立」而不是睡一段固定时间。首次扫描要现场生成 Moq 代理、
    // 编译表达式，满载机器上单这一下就能超过 200ms；按固定窗口数扫描次数，量到的是机器
    // 有多忙，不是调度器在不在跑。

    /// <summary>
    /// 等调度器条件成立的上限。条件一成立立即返回，只有调度器真的停了（或从没跑起来）
    /// 才会走满；按满载机器定，单跑时这几条测试只要几十毫秒。
    /// </summary>
    private static readonly TimeSpan SchedulerBound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Scheduler_BuiltInDisabled_StopsWithoutScanning()
    {
        // 关掉自带循环（改用 Hangfire 之类）时不能再自己扫，但也不能沉默地停 ——
        // 那样"关掉了"和"根本没接上"在日志里长得一样。
        var scanned = 0;
        var host = CreateHostedService(
            new WorkflowWatchdogOptions { UseBuiltInScheduler = false, ScanInterval = TimeSpan.FromMilliseconds(10) },
            () => Interlocked.Increment(ref scanned));

        await host.StartAsync(CancellationToken.None);

        // 「停下」可以直接观察：循环自己结束了。结束之后它再也不可能扫描，零次才是确定的结论。
        var finished = await Task.WhenAny(host.ExecuteTask!, Task.Delay(SchedulerBound));
        await host.StopAsync(CancellationToken.None);

        finished.ShouldBe(host.ExecuteTask, "the built-in loop must end on its own when it is switched off");
        scanned.ShouldBe(0);
    }

    [Fact]
    public async Task Scheduler_Enabled_ScansOnItsInterval()
    {
        // 两次而不是一次：一次只说明启动时扫过，按间隔反复扫才是这个调度器存在的理由。
        var probe = new ScanProbe(target: 2);
        var host = CreateHostedService(
            new WorkflowWatchdogOptions { ScanInterval = TimeSpan.FromMilliseconds(20) },
            probe.Record);

        await host.StartAsync(CancellationToken.None);
        await AwaitScansAsync(host, probe);
    }

    [Fact]
    public async Task Scheduler_ScanThrows_KeepsRunning()
    {
        // 一次扫描失败让循环退出是没有任何症状的失效：超时检测再也不发生，日志里只有一行。
        // 每次扫描都抛，等到第三次：熬过连续两次失败，排除「只重试一次就放弃」。
        var probe = new ScanProbe(target: 3);
        var host = CreateHostedService(
            new WorkflowWatchdogOptions { ScanInterval = TimeSpan.FromMilliseconds(20) },
            () =>
            {
                probe.Record();
                throw new InvalidOperationException("boom");
            });

        await host.StartAsync(CancellationToken.None);
        await AwaitScansAsync(host, probe);
    }

    /// <summary>
    /// 等到扫描达到目标次数，然后停掉调度器。循环先结束（<c>ExecuteTask</c> 完成）立即判失败，
    /// 不必等满上限：那正是「调度器停了」本身。
    /// </summary>
    private static async Task AwaitScansAsync(WorkflowWatchdogHostedService host, ScanProbe probe)
    {
        try
        {
            var finished = await Task.WhenAny(probe.Reached, host.ExecuteTask!, Task.Delay(SchedulerBound));

            finished.ShouldNotBe(
                host.ExecuteTask,
                $"the scheduler loop ended ({host.ExecuteTask!.Status}: {host.ExecuteTask.Exception?.GetBaseException().Message}) after {probe.Calls} scan(s)");
            finished.ShouldBe(probe.Reached, $"the scheduler scanned only {probe.Calls} time(s) within {SchedulerBound}");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>数扫描次数，到达目标次数时发信号。</summary>
    private sealed class ScanProbe(int target)
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public Task Reached => _reached.Task;

        public int Calls => Volatile.Read(ref _calls);

        public void Record()
        {
            if (Interlocked.Increment(ref _calls) >= target)
            {
                _reached.TrySetResult();
            }
        }
    }

    /// <summary>
    /// 造一个 hosted service，其 scope 解析出的 <see cref="WorkflowWatchdogService"/>
    /// 会在 <c>ScanAsync</c> 时回调 <paramref name="onScan"/>（用一个会被谓词命中的假仓储实现）。
    /// </summary>
    private static WorkflowWatchdogHostedService CreateHostedService(
        WorkflowWatchdogOptions options, Action onScan)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ =>
        {
            var repo = new Mock<IRepository<WorkflowExecution, Guid>>();
            repo.Setup(r => r.AsQueryable()).Returns(() =>
            {
                onScan();
                return new List<WorkflowExecution>().BuildMock();
            });

            return new WorkflowWatchdogService(
                repo.Object,
                NullLogger<WorkflowWatchdogService>.Instance,
                new StaticOptionsMonitor<WorkflowWatchdogOptions>(options));
        });

        var provider = services.BuildServiceProvider();

        return new WorkflowWatchdogHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<WorkflowWatchdogOptions>(options),
            NullLogger<WorkflowWatchdogHostedService>.Instance);
    }
}
