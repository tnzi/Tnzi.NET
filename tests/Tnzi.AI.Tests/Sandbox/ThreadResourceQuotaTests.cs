using Microsoft.Extensions.Caching.Memory;
using Tnzi.AI.Sandbox.Events;
using Tnzi.AI.Sandbox.Options;
using Tnzi.AI.Sandbox.Quota;
using Tnzi.AI.Sandbox.Tools;
using Tnzi.Caching;
using Tnzi.EventBus;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Integration tests for <see cref="ThreadResourceQuotaService"/> driven by a
/// real in-memory <see cref="MemoryCacheService"/> (no mocks) so the
/// <c>IncrementAsync</c> atomicity and TTL semantics are actually exercised.
///
/// Also covers the <see cref="SandboxTools.BashAsync"/> integration: quota
/// denial must funnel through the same event publication path as
/// command-blacklist denial so audit dashboards see a unified denial stream.
/// </summary>
public class ThreadResourceQuotaTests : IDisposable
{
    private readonly MemoryCache _memoryCache;
    private readonly MemoryCacheService _cache;
    private readonly Guid _threadId = Guid.NewGuid();

    public ThreadResourceQuotaTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cache = new MemoryCacheService(
            _memoryCache,
            NullLogger<MemoryCacheService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new CachingOptions { DefaultExpirationMinutes = 60 }));
    }

    public void Dispose()
    {
        _cache.Dispose();
        _memoryCache.Dispose();
    }

    private ThreadResourceQuotaService CreateQuota(ThreadQuotaOptions? overrides = null)
    {
        var opts = new SandboxModuleOptions { ThreadQuota = overrides ?? new ThreadQuotaOptions() };
        return new ThreadResourceQuotaService(
            _cache,
            Microsoft.Extensions.Options.Options.Create(opts),
            NullLogger<ThreadResourceQuotaService>.Instance);
    }

    // ------------------------------------------------------------------ //
    // Quota service unit behaviour
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Disabled_AllowsAll_AndSkipsAccounting()
    {
        var quota = CreateQuota(new ThreadQuotaOptions { Enabled = false, MaxCommandCount = 1 });

        var check = await quota.CheckAsync(_threadId);
        check.IsAllowed.ShouldBeTrue();
        check.RemainingCommands.ShouldBe(long.MaxValue);

        // Recording on a disabled quota is a no-op — usage stays zero.
        await quota.RecordExecutionAsync(_threadId, 100, 100);
        var usage = await quota.GetUsageAsync(_threadId);
        usage.CommandCount.ShouldBe(0);
    }

    [Fact]
    public async Task FreshThread_AllowsWithFullRemaining()
    {
        var quota = CreateQuota(new ThreadQuotaOptions
        {
            MaxCommandCount = 100,
            MaxTotalDurationMs = 5_000,
            MaxTotalOutputBytes = 10_240
        });

        var check = await quota.CheckAsync(_threadId);

        check.IsAllowed.ShouldBeTrue();
        check.RemainingCommands.ShouldBe(100);
        check.RemainingDurationMs.ShouldBe(5_000);
        check.RemainingOutputBytes.ShouldBe(10_240);
    }

    [Fact]
    public async Task RecordExecution_AccumulatesAllThreeCounters()
    {
        var quota = CreateQuota();

        await quota.RecordExecutionAsync(_threadId, durationMs: 120, outputBytes: 512);
        await quota.RecordExecutionAsync(_threadId, durationMs: 80, outputBytes: 1024);

        var usage = await quota.GetUsageAsync(_threadId);
        usage.CommandCount.ShouldBe(2);
        usage.TotalDurationMs.ShouldBe(200);
        usage.TotalOutputBytes.ShouldBe(1536);
    }

    [Fact]
    public async Task ExceedingCommandCount_Denies()
    {
        var quota = CreateQuota(new ThreadQuotaOptions { MaxCommandCount = 2 });

        await quota.RecordExecutionAsync(_threadId, 1, 1);
        await quota.RecordExecutionAsync(_threadId, 1, 1);

        var check = await quota.CheckAsync(_threadId);
        check.IsAllowed.ShouldBeFalse();
        check.Reason!.ShouldContain("command count");
    }

    [Fact]
    public async Task ExceedingDuration_Denies()
    {
        var quota = CreateQuota(new ThreadQuotaOptions { MaxTotalDurationMs = 500 });

        await quota.RecordExecutionAsync(_threadId, durationMs: 300, outputBytes: 0);
        await quota.RecordExecutionAsync(_threadId, durationMs: 250, outputBytes: 0);

        var check = await quota.CheckAsync(_threadId);
        check.IsAllowed.ShouldBeFalse();
        check.Reason!.ShouldContain("duration");
    }

    [Fact]
    public async Task ExceedingOutputBytes_Denies()
    {
        var quota = CreateQuota(new ThreadQuotaOptions { MaxTotalOutputBytes = 1_000 });

        await quota.RecordExecutionAsync(_threadId, durationMs: 0, outputBytes: 600);
        await quota.RecordExecutionAsync(_threadId, durationMs: 0, outputBytes: 600);

        var check = await quota.CheckAsync(_threadId);
        check.IsAllowed.ShouldBeFalse();
        check.Reason!.ShouldContain("output");
    }

    [Fact]
    public async Task ZeroLimit_DisablesIndividualCap()
    {
        // Setting MaxCommandCount=0 should disable just that dimension, leaving
        // duration and output caps active.
        var quota = CreateQuota(new ThreadQuotaOptions
        {
            MaxCommandCount = 0,
            MaxTotalDurationMs = 1_000,
            MaxTotalOutputBytes = 1_000
        });

        for (var i = 0; i < 50; i++)
            await quota.RecordExecutionAsync(_threadId, durationMs: 1, outputBytes: 1);

        var check = await quota.CheckAsync(_threadId);
        check.IsAllowed.ShouldBeTrue();
        check.RemainingCommands.ShouldBe(long.MaxValue);
    }

    [Fact]
    public async Task Reset_ClearsCounters()
    {
        var quota = CreateQuota(new ThreadQuotaOptions { MaxCommandCount = 2 });

        await quota.RecordExecutionAsync(_threadId, 1, 1);
        await quota.RecordExecutionAsync(_threadId, 1, 1);
        (await quota.CheckAsync(_threadId)).IsAllowed.ShouldBeFalse();

        await quota.ResetAsync(_threadId);

        var usage = await quota.GetUsageAsync(_threadId);
        usage.CommandCount.ShouldBe(0);
        (await quota.CheckAsync(_threadId)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task DifferentThreads_DoNotShareCounters()
    {
        var quota = CreateQuota(new ThreadQuotaOptions { MaxCommandCount = 1 });
        var otherThread = Guid.NewGuid();

        await quota.RecordExecutionAsync(_threadId, 1, 1);

        (await quota.CheckAsync(_threadId)).IsAllowed.ShouldBeFalse();
        (await quota.CheckAsync(otherThread)).IsAllowed.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ //
    // SandboxTools integration
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task BashAsync_NoQuotaInjected_ExecutesNormally()
    {
        await using var fixture = new SandboxToolsFixture(_threadId);
        var tools = new SandboxTools(fixture.Translator, NullLogger<SandboxTools>.Instance, quota: null);

        var result = await tools.BashAsync(fixture.Sandbox, _threadId, "echo no-quota");

        result.ToString()!.ShouldContain("no-quota");
    }

    [Fact]
    public async Task BashAsync_UnderQuota_ExecutesAndAccumulatesUsage()
    {
        await using var fixture = new SandboxToolsFixture(_threadId);
        var quota = CreateQuota(new ThreadQuotaOptions { MaxCommandCount = 10 });
        var tools = new SandboxTools(fixture.Translator, NullLogger<SandboxTools>.Instance, quota: quota);

        await tools.BashAsync(fixture.Sandbox, _threadId, "echo first");
        await tools.BashAsync(fixture.Sandbox, _threadId, "echo second");

        var usage = await quota.GetUsageAsync(_threadId);
        usage.CommandCount.ShouldBe(2);
        usage.TotalOutputBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task BashAsync_OverCommandCap_DeniesAndPublishesEventAsDenied()
    {
        await using var fixture = new SandboxToolsFixture(_threadId);
        var quota = CreateQuota(new ThreadQuotaOptions { MaxCommandCount = 1 });
        var bus = new CapturingEventBus();
        var tools = new SandboxTools(
            fixture.Translator,
            NullLogger<SandboxTools>.Instance,
            eventBus: bus,
            quota: quota);

        // First call succeeds — fills the single-command quota.
        await tools.BashAsync(fixture.Sandbox, _threadId, "echo first");
        bus.Captured.OfType<SandboxCommandExecutedEvent>().Single().Denied.ShouldBeFalse();

        // Second call is denied by the quota pre-flight check.
        bus.Captured.Clear();
        var secondResult = await tools.BashAsync(fixture.Sandbox, _threadId, "echo second");

        secondResult.ToString()!.ShouldContain("Command denied", Case.Insensitive);
        var deniedEvent = bus.Captured.OfType<SandboxCommandExecutedEvent>().Single();
        deniedEvent.Denied.ShouldBeTrue();
        deniedEvent.DenialReason!.ShouldContain("command count");
        deniedEvent.ExitCode.ShouldBe(-1);
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private sealed class SandboxToolsFixture : IAsyncDisposable
    {
        public string WorkDir { get; }
        public VirtualPathTranslator Translator { get; }
        public LocalSandbox Sandbox { get; }

        public SandboxToolsFixture(Guid threadId)
        {
            WorkDir = Path.Combine(Path.GetTempPath(), $"tnzi-quota-test-{Guid.NewGuid():N}");
            var threadDir = Path.Combine(WorkDir, threadId.ToString("N"));
            Directory.CreateDirectory(Path.Combine(threadDir, "workspace"));

            Translator = new VirtualPathTranslator(WorkDir);
            Sandbox = new LocalSandbox(
                id: "quota-test",
                workspacePath: Path.Combine(threadDir, "workspace"),
                commandTimeout: TimeSpan.FromSeconds(5),
                maxOutputSize: 1024);
        }

        public async ValueTask DisposeAsync()
        {
            await Sandbox.DisposeAsync();
            if (Directory.Exists(WorkDir))
                Directory.Delete(WorkDir, recursive: true);
        }
    }

    private sealed class CapturingEventBus : IEventBus
    {
        public List<IEvent> Captured { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent
        {
            Captured.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default) where TEvent : class, IEvent
        {
            Captured.Add(@event);
            return Task.CompletedTask;
        }

        public bool HasHandlers<TEvent>() where TEvent : class, IEvent => false;
        public int GetHandlerCount<TEvent>() where TEvent : class, IEvent => 0;
        public void Subscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void Unsubscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent { }
    }
}
