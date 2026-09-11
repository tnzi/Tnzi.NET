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

    [Fact]
    public async Task Scheduler_BuiltInDisabled_StopsWithoutScanning()
    {
        // 关掉自带循环（改用 Hangfire 之类）时不能再自己扫，但也不能沉默地停 ——
        // 那样"关掉了"和"根本没接上"在日志里长得一样。
        var scanned = 0;
        var host = CreateHostedService(
            new WorkflowWatchdogOptions { UseBuiltInScheduler = false, ScanInterval = TimeSpan.FromMilliseconds(10) },
            () => scanned++);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await host.StartAsync(cts.Token);
        await Task.Delay(80, CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        scanned.ShouldBe(0);
    }

    [Fact]
    public async Task Scheduler_Enabled_ScansOnItsInterval()
    {
        var scanned = 0;
        var host = CreateHostedService(
            new WorkflowWatchdogOptions { ScanInterval = TimeSpan.FromMilliseconds(20) },
            () => Interlocked.Increment(ref scanned));

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(200, CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        scanned.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Scheduler_ScanThrows_KeepsRunning()
    {
        // 一次扫描失败让循环退出是没有任何症状的失效：超时检测再也不发生，日志里只有一行。
        var calls = 0;
        var host = CreateHostedService(
            new WorkflowWatchdogOptions { ScanInterval = TimeSpan.FromMilliseconds(20) },
            () =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("boom");
            });

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(200, CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        calls.ShouldBeGreaterThan(1);
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
