using Microsoft.Extensions.Logging;
using Tnzi.Audit.Tests.TestSupport;
using Tnzi.Locking;

namespace Tnzi.Audit.Tests.Retention;

/// <summary>
/// <see cref="AuditRetentionBackgroundService"/>：按 <c>Audit:RetentionDays</c> 定时删过期操作审计的那一轮。
/// </summary>
/// <remarks>
/// <para>
/// 在它之前 <c>RetentionDays</c> 的唯一读者是手动清理端点的缺省参数，配置中心却把它挂在「Retention」组下 ——
/// 操作者把天数从 90 改成 30，保存成功、热读拿到新值、然后什么都不发生。
/// </para>
/// <para>
/// 被测对象是真实的后台服务（经 <c>RunOnceAsync</c> 驱动一轮）；<see cref="IAuditStore"/> 用桩记录它被要求删多少天前的行，
/// 删除本身由 <c>DatabaseAuditStoreIntegrationTests</c> 对真库覆盖。
/// </para>
/// </remarks>
public class AuditRetentionBackgroundServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AuditOptions.MaxAutoPurgeIntervalHours + 1)]
    public void Validator_RejectsAnIntervalTheLoopCannotSleep(int hours)
    {
        // Task.Delay 等不了 49 天以上；放行一个写大了的间隔 = 第一轮之后后台服务抛异常、宿主停机。
        var result = new AuditOptionsValidator().Validate(null, new AuditOptions { AutoPurgeIntervalHours = hours });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(AuditOptions.AutoPurgeIntervalHours));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(AuditOptions.MaxAutoPurgeIntervalHours)]
    public void Validator_AcceptsAnIntervalWithinTheBounds(int hours)
    {
        new AuditOptionsValidator().Validate(null, new AuditOptions { AutoPurgeIntervalHours = hours }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task RunOnce_DeletesOperationsOlderThanTheConfiguredRetention()
    {
        var store = new RecordingStore(deleted: 12);
        var (service, logger) = Create(store, new AuditOptions { AutoPurgeEnabled = true, RetentionDays = 30 });

        var deleted = await service.RunOnceAsync(CancellationToken.None);

        deleted.ShouldBe(12);
        store.RequestedDays.ShouldBe([30]);
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("removed 12"));
    }

    [Fact]
    public async Task RunOnce_ReadsTheLatestRetentionDaysOnEveryCycle()
    {
        // 热设置：管理员刚改的天数应当在下一轮就生效，而不是停在启动时读到的那个值。
        var store = new RecordingStore(deleted: 0);
        var monitor = new MutableOptionsMonitor(new AuditOptions { AutoPurgeEnabled = true, RetentionDays = 90 });
        var service = Create(store, monitor, new CapturingLogger());

        await service.RunOnceAsync(CancellationToken.None);
        monitor.CurrentValue = new AuditOptions { AutoPurgeEnabled = true, RetentionDays = 7 };
        await service.RunOnceAsync(CancellationToken.None);

        store.RequestedDays.ShouldBe([90, 7]);
    }

    [Fact]
    public async Task RunOnce_WhenAnotherInstanceHoldsTheLock_SkipsWithoutDeleting()
    {
        var store = new RecordingStore(deleted: 5);
        var (service, logger) = Create(store, new AuditOptions { AutoPurgeEnabled = true }, new StubLock(acquired: false));

        var deleted = await service.RunOnceAsync(CancellationToken.None);

        deleted.ShouldBe(0);
        store.RequestedDays.ShouldBeEmpty();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("another instance holds the lock"));
        logger.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task RunOnce_WithTheLock_DeletesAndReleasesIt()
    {
        var store = new RecordingStore(deleted: 3);
        var @lock = new StubLock(acquired: true);
        var (service, _) = Create(store, new AuditOptions { AutoPurgeEnabled = true, RetentionDays = 45 }, @lock);

        var deleted = await service.RunOnceAsync(CancellationToken.None);

        deleted.ShouldBe(3);
        store.RequestedDays.ShouldBe([45]);
        @lock.Key.ShouldBe(AuditRetentionBackgroundService.PurgeLockKey);
        @lock.Handle!.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task RunOnce_WithANonPositiveRetention_DeletesNothing()
    {
        // 校验器在启动时挡掉 <= 0；这里防的是热改成非法值后把「0 天前」当成全表。
        var store = new RecordingStore(deleted: 999);
        var (service, logger) = Create(store, new AuditOptions { AutoPurgeEnabled = true, RetentionDays = 0 });

        var deleted = await service.RunOnceAsync(CancellationToken.None);

        deleted.ShouldBe(0);
        store.RequestedDays.ShouldBeEmpty();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_WhileDisabled_NeverTouchesTheStore()
    {
        // 开关关着是出厂状态：服务在场但一行不删。停止它时循环要干净退出。
        var store = new RecordingStore(deleted: 1);
        var (service, _) = Create(store, new AuditOptions { AutoPurgeEnabled = false, RetentionDays = 1 });

        using var stop = new CancellationTokenSource();
        var running = service.StartAsync(stop.Token);
        await running;
        await stop.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        store.RequestedDays.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheConfigurationIsInvalid_KeepsRunningInsteadOfStoppingTheHost()
    {
        // 热重载成非法值后 CurrentValue 每次都抛。异常逃出 ExecuteAsync = 后台服务故障 = 默认 StopHost。
        var store = new RecordingStore(deleted: 1);
        var logger = new CapturingLogger();
        var service = Create(store, new ThrowingOptionsMonitor(), logger);

        using var stop = new CancellationTokenSource();
        await service.StartAsync(stop.Token);
        Assert.NotNull(service.ExecuteTask);

        // ExecuteAsync 在后台线程上跑：等到它要么记下那条 Error、要么已经结束（未修复时它会带着异常结束）。
        bool Logged()
        {
            lock (logger.Entries)
            {
                return logger.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("configuration is currently invalid"));
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!Logged() && !service.ExecuteTask!.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        service.ExecuteTask!.IsFaulted.ShouldBeFalse();
        Logged().ShouldBeTrue();

        await stop.CancelAsync();
        await service.StopAsync(CancellationToken.None);
        service.ExecuteTask.IsFaulted.ShouldBeFalse();
        store.RequestedDays.ShouldBeEmpty();
    }

    private sealed class ThrowingOptionsMonitor : IOptionsMonitor<AuditOptions>
    {
        public AuditOptions CurrentValue
            => throw new OptionsValidationException(Microsoft.Extensions.Options.Options.DefaultName, typeof(AuditOptions), ["AutoPurgeIntervalHours must be between 1 and 720."]);

        public AuditOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuditOptions, string?> listener) => null;
    }

    private static (AuditRetentionBackgroundService Service, CapturingLogger Logger) Create(
        IAuditStore store, AuditOptions options, IDistributedLock? distributedLock = null)
    {
        var logger = new CapturingLogger();
        return (Create(store, new StaticOptionsMonitor<AuditOptions>(options), logger, distributedLock), logger);
    }

    private static AuditRetentionBackgroundService Create(
        IAuditStore store, IOptionsMonitor<AuditOptions> options, CapturingLogger logger, IDistributedLock? distributedLock = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        if (distributedLock is not null)
        {
            services.AddSingleton(distributedLock);
        }

        var provider = services.BuildServiceProvider();
        return new AuditRetentionBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), options, logger);
    }

    private sealed class RecordingStore(int deleted) : IAuditStore
    {
        public List<int> RequestedDays { get; } = [];

        public Task SaveOperationAsync(AuditOperation operation) => throw new NotSupportedException();
        public Task SaveOperationBatchAsync(IEnumerable<AuditOperation> operations) => throw new NotSupportedException();
        public Task SaveEntityEntriesAsync(IEnumerable<AuditEntityEntry> entries) => throw new NotSupportedException();

        public Task<int> DeleteExpiredAsync(int days, CancellationToken cancellationToken = default)
        {
            RequestedDays.Add(days);
            return Task.FromResult(deleted);
        }
    }

    private sealed class MutableOptionsMonitor(AuditOptions initial) : IOptionsMonitor<AuditOptions>
    {
        public AuditOptions CurrentValue { get; set; } = initial;
        public AuditOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuditOptions, string?> listener) => null;
    }

    private sealed class StubLock(bool acquired) : IDistributedLock
    {
        public string? Key { get; private set; }
        public StubHandle? Handle { get; private set; }

        public Task<IDistributedLockHandle?> AcquireAsync(string key, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Key = key;
            if (!acquired)
            {
                return Task.FromResult<IDistributedLockHandle?>(null);
            }

            Handle = new StubHandle(key);
            return Task.FromResult<IDistributedLockHandle?>(Handle);
        }

        public Task<(bool Success, IDistributedLockHandle? Handle)> TryAcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubHandle(string key) : IDistributedLockHandle
    {
        public string Key => key;
        public bool IsAcquired => true;
        public CancellationToken Lost => CancellationToken.None;
        public bool Disposed { get; private set; }

        public Task<bool> ExtendAsync(TimeSpan extension) => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<AuditRetentionBackgroundService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
