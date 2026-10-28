namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 停机时队列处理器等进行中的执行收尾（受 <c>HostOptions.ShutdownTimeout</c> 约束），收尾之后才释放并发槽。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：循环一退出就释放并发槽、<c>ExecuteAsync</c> 随即返回，宿主不等进行中的执行就继续停机。
/// 执行器「停机就把运行打回队列」那一步赶不上，运行停在 Dispatched / Running，只能等租约过期才被回收；
/// 执行随后在 finally 里归还一个已经释放的槽，抛出没人观察的 <see cref="ObjectDisposedException"/>。
/// </para>
/// <para>
/// 这里把执行换成一个可控的替身：收到停机取消之后再慢慢收尾（打回队列要写库，不是瞬间的事）。
/// 运行时行不存在时认领不拦，所以只需要一条排队的运行。
/// </para>
/// </remarks>
public class CliRunQueueShutdownTests : IntegratedTestBase<CliQueueDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliQueueDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliQueueDbContext, CliRuntime, Guid>>();
    }

    [Fact]
    public async Task Stopping_waits_for_the_in_flight_run_to_finish_stopping()
    {
        await SeedQueuedRunAsync();
        var processor = CreateProcessor(releaseDelay: TimeSpan.FromMilliseconds(500), out var logger);

        await processor.StartAsync(CancellationToken.None);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await processor.StopAsync(shutdown.Token);

        processor.Released.ShouldBeTrue("stopping must wait until the in-flight run has stopped");
        processor.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
        logger.Errors.ShouldBeEmpty();
    }

    /// <summary>
    /// 收尾比停机时限还慢时，停机按时限返回；执行之后照常收尾，归还并发槽时不撞上一个已释放的槽。
    /// </summary>
    [Fact]
    public async Task A_run_slower_than_the_shutdown_timeout_does_not_hold_the_host_and_still_returns_its_slot_cleanly()
    {
        await SeedQueuedRunAsync();
        var releaseDelay = TimeSpan.FromSeconds(3);
        var processor = CreateProcessor(releaseDelay, out var logger);

        await processor.StartAsync(CancellationToken.None);
        await processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stopwatch = Stopwatch.StartNew();
        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await processor.StopAsync(shutdown.Token);
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(releaseDelay - TimeSpan.FromMilliseconds(500), "stopping is bounded by the shutdown timeout");
        processor.Released.ShouldBeFalse();

        // 执行在时限之后照常收尾，ExecuteAsync 等到它之后才释放并发槽
        await processor.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));
        processor.Released.ShouldBeTrue();
        processor.ExecuteTask.IsCompletedSuccessfully.ShouldBeTrue();
        logger.Errors.ShouldBeEmpty("returning the slot after shutdown must not hit a disposed semaphore");
    }

    private async Task SeedQueuedRunAsync()
    {
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = CliRunStatus.Queued,
            Prompt = "summarise the repository"
        });
        await DbContext.SaveChangesAsync();
    }

    private SlowStoppingProcessor CreateProcessor(TimeSpan releaseDelay, out CapturingLogger logger)
    {
        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        // 租约很长：续租不在测试期间触发（SQLite 内存库只有一条连接，不让它与认领循环并发访问）
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions
        {
            Enabled = true,
            PollInterval = TimeSpan.FromMilliseconds(20),
            LeaseDuration = TimeSpan.FromMinutes(30)
        });

        logger = new CapturingLogger();
        return new SlowStoppingProcessor(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            options.Object,
            logger,
            releaseDelay);
    }

    /// <summary>执行替身：一直跑到收到取消，然后花 <c>releaseDelay</c> 收尾（打回队列要写库）。</summary>
    private sealed class SlowStoppingProcessor(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliRunQueueProcessor> logger,
        TimeSpan releaseDelay)
        : CliRunQueueProcessor(scopeFactory, new CliRunSignalHub(), new CliRunCancellationRegistry(), options, logger)
    {
        private volatile bool _released;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Released => _released;

        internal override async Task ExecuteRunAsync(Guid runId, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 停机：接着收尾
            }

            await Task.Delay(releaseDelay, CancellationToken.None);
            _released = true;
        }
    }

    private sealed class CapturingLogger : ILogger<CliRunQueueProcessor>
    {
        private readonly List<string> _errors = [];

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_errors)
                {
                    return [.. _errors];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
                return;

            lock (_errors)
            {
                _errors.Add($"{formatter(state, exception)} {exception?.GetType().Name}");
            }
        }
    }
}
