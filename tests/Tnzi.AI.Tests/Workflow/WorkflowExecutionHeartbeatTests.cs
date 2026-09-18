using System.Globalization;
using Microsoft.Data.Sqlite;
using Tnzi.AI.Workflow.Options;
using Tnzi.EFCore.Data;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// The watchdog reaps <c>Running</c> rows whose <c>UpdatedTime</c> is older than
/// <c>RunningTimeout</c> (30 min by default). Until 2026-09-12 nothing touched that column
/// while a run was in flight: Sequential / Parallel runs never wrote it after the insert and
/// DAG runs only at layer boundaries, so any execution (or single node: TimeoutSeconds goes
/// up to 3600, with retries) longer than the timeout was marked Failed / timed_out while it
/// was still executing and billing tokens. The engine now drives a periodic heartbeat for
/// the whole time it executes, in every mode, independent of the checkpoint store.
/// </summary>
public class WorkflowExecutionHeartbeatEngineTests
{
    [Fact]
    public async Task ExecuteAsync_BeatsRepeatedlyWhileASingleLongNodeRuns_AndStopsWhenTheRunEnds()
    {
        var heartbeat = new RecordingHeartbeat(TimeSpan.FromMilliseconds(40));
        var serviceProvider = BuildServices(heartbeat, nodeDelay: TimeSpan.FromMilliseconds(400));
        var engine = new WorkflowEngine(NullLogger<WorkflowEngine>.Instance);

        await engine.ExecuteAsync(SingleNodeGraph(), "input", serviceProvider, new WorkflowExecutionOptions
        {
            ExecutionId = "exec-heartbeat"
        });

        heartbeat.Beats.Count.ShouldBeGreaterThanOrEqualTo(3,
            "one 400 ms node with a 40 ms interval must produce several beats; node boundaries alone would give none");
        heartbeat.Beats.ShouldAllBe(id => id == "exec-heartbeat");

        var countAtReturn = heartbeat.Beats.Count;
        await Task.Delay(200);
        heartbeat.Beats.Count.ShouldBe(countAtReturn, "the loop must stop with the run; a beat after the terminal update would resurrect the row's freshness");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutAnExecutionId_DoesNotBeat()
    {
        // No ExecutionId means no WorkflowExecution row to keep alive.
        var heartbeat = new RecordingHeartbeat(TimeSpan.FromMilliseconds(20));
        var serviceProvider = BuildServices(heartbeat, nodeDelay: TimeSpan.FromMilliseconds(150));
        var engine = new WorkflowEngine(NullLogger<WorkflowEngine>.Instance);

        await engine.ExecuteAsync(SingleNodeGraph(), "input", serviceProvider);

        heartbeat.Beats.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_AFailingBeat_IsLoggedAndDoesNotFailTheRun()
    {
        var heartbeat = new RecordingHeartbeat(TimeSpan.FromMilliseconds(30)) { Throw = true };
        var serviceProvider = BuildServices(heartbeat, nodeDelay: TimeSpan.FromMilliseconds(200));
        var logger = new CapturingEngineLogger();
        var engine = new WorkflowEngine(logger);

        var result = await engine.ExecuteAsync(SingleNodeGraph(), "input", serviceProvider, new WorkflowExecutionOptions
        {
            ExecutionId = "exec-heartbeat-fail"
        });

        result.HasFailure.ShouldBeFalse("a heartbeat is observability, never a reason to fail the workflow");
        heartbeat.Beats.Count.ShouldBeGreaterThanOrEqualTo(2, "one failure must not stop the loop");
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("exec-heartbeat-fail"));
    }

    private static WorkflowGraph SingleNodeGraph() => new(
    [
        new WorkflowStepDto { StepId = "slow", Configuration = new Dictionary<string, string> { ["nodeType"] = "slow" } }
    ]);

    private static IServiceProvider BuildServices(IWorkflowExecutionHeartbeat heartbeat, TimeSpan nodeDelay)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(heartbeat);
        services.AddScoped<IWorkflowNode>(_ => new SlowNode(nodeDelay));
        services.AddScoped<WorkflowNodeExecutor>();
        return services.BuildServiceProvider();
    }

    private sealed class SlowNode(TimeSpan delay) : IWorkflowNode
    {
        public string NodeType => "slow";

        public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return new WorkflowNodeResult { Output = "slow-done", IsSuccess = true };
        }
    }

    private sealed class CapturingEngineLogger : ILogger<WorkflowEngine>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>Records every beat; optionally throws to prove failures are contained.</summary>
internal sealed class RecordingHeartbeat(TimeSpan interval) : IWorkflowExecutionHeartbeat
{
    private readonly List<string> _beats = [];
    public bool Throw { get; init; }
    public TimeSpan Interval => interval;
    public IReadOnlyList<string> Beats
    {
        get { lock (_beats) return _beats.ToList(); }
    }

    public Task BeatAsync(string executionId, CancellationToken cancellationToken = default)
    {
        lock (_beats) _beats.Add(executionId);
        if (Throw) throw new InvalidOperationException("simulated heartbeat failure");
        return Task.CompletedTask;
    }
}

/// <summary>
/// The service layer hands the engine its own scoped provider; the heartbeat must be
/// reachable from there in every execution mode, and the false-reap aftermath must be
/// repaired by the run's own terminal update.
/// </summary>
public class WorkflowExecutionHeartbeatServiceTests : WorkflowServiceSqliteTestBase
{
    private readonly RecordingHeartbeat _heartbeat = new(TimeSpan.FromMilliseconds(30));

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddSingleton<IWorkflowExecutionHeartbeat>(_heartbeat);
        services.AddScoped<IWorkflowNode, ReapingNode>();
    }

    [Theory]
    [InlineData(WorkflowExecutionMode.Sequential)]
    [InlineData(WorkflowExecutionMode.Parallel)]
    [InlineData(WorkflowExecutionMode.Dag)]
    public async Task RunAsync_DrivesTheHeartbeatForTheExecutionRow_InEveryMode(WorkflowExecutionMode mode)
    {
        var definition = await InsertDefinitionAsync("""
            [ { "stepId": "a", "configuration": { "nodeType": "reaping", "delayMs": "150" } } ]
            """, mode);

        var run = await CreateService().RunAsync(definition.Id, "start");

        run.Succeeded.ShouldBeTrue(run.Message);
        _heartbeat.Beats.ShouldNotBeEmpty("Sequential and Parallel runs used to write nothing between insert and completion");
        _heartbeat.Beats.ShouldAllBe(id => id == run.Data!.ExecutionId);
    }

    [Fact]
    public async Task RunAsync_WhenTheWatchdogFalselyReapedTheRowMidRun_TheTerminalUpdateWinsAndRepairsIt()
    {
        // The node simulates a watchdog scan that reaped this very row while it was executing
        // (Failed / timed_out / CompletedTime stamped, concurrency stamp bumped by that write).
        var definition = await InsertDefinitionAsync("""
            [ { "stepId": "a", "configuration": { "nodeType": "reaping", "delayMs": "50", "reap": "true" } } ]
            """, WorkflowExecutionMode.Sequential);
        var before = DateTime.UtcNow;

        var run = await CreateService().RunAsync(definition.Id, "start");
        run.Succeeded.ShouldBeTrue(run.Message);

        var row = await LoadExecutionAsync(run.Data!.ExecutionId!);
        row.ShouldNotBeNull();
        row!.Status.ShouldBe(WorkflowExecutionStatus.Completed, "the run's own outcome is authoritative over a false reap");
        row.CurrentWaitReason.ShouldBeNull("the watchdog's timed_out marker must not survive");
        row.CompletedTime.ShouldNotBeNull();
        row.CompletedTime!.Value.ShouldBeGreaterThanOrEqualTo(before, "the false CompletedTime the watchdog stamped must be overwritten");
        row.DurationMs.ShouldBe((long?)(row.CompletedTime.Value - row.StartedAt!.Value).TotalMilliseconds);
    }

    /// <summary>Sleeps, and on request stamps every Running row the way the watchdog does, from its own scope.</summary>
    private sealed class ReapingNode(IServiceScopeFactory scopeFactory) : IWorkflowNode
    {
        public string NodeType => "reaping";

        public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            var configuration = context.Step.Configuration ?? new Dictionary<string, string>();
            var delay = int.Parse(configuration.GetValueOrDefault("delayMs") ?? "0", CultureInfo.InvariantCulture);
            await Task.Delay(delay, cancellationToken);

            if (string.Equals(configuration.GetValueOrDefault("reap"), "true", StringComparison.OrdinalIgnoreCase))
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowExecution, Guid>>();
                var running = await repository.ToListAsync(e => e.Status == WorkflowExecutionStatus.Running, cancellationToken);
                foreach (var row in running)
                {
                    row.Status = WorkflowExecutionStatus.Failed;
                    row.CurrentWaitReason = "timed_out";
                    row.CompletedTime = DateTime.UtcNow.AddMinutes(-10);
                    row.UpdatedTime = row.CompletedTime.Value;
                    await repository.UpdateAsync(row, cancellationToken);
                }
            }

            return new WorkflowNodeResult { Output = "done", IsSuccess = true };
        }
    }
}

/// <summary>
/// The database heartbeat bumps <c>UpdatedTime</c> with a set-based update: it must not touch
/// the concurrency stamp (the request scope's checkpoint save relies on it) and it must reach
/// a tenant's row from the engine's scope even when multi-tenancy is on.
/// </summary>
public sealed class DatabaseWorkflowExecutionHeartbeatTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DatabaseWorkflowExecutionHeartbeatTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var seed = CreateContext(null);
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task BeatAsync_BumpsUpdatedTime_KeepsTheConcurrencyStamp_AndReachesTenantRows()
    {
        var tenant = Guid.NewGuid();
        var stale = DateTime.UtcNow.AddHours(-1);
        var tenantRow = new WorkflowExecution { ExecutionId = "tenant-row", Status = WorkflowExecutionStatus.Running, UpdatedTime = stale, TenantId = tenant };
        using (var seed = CreateContext(null))
        {
            seed.Set<WorkflowExecution>().AddRange(
                tenantRow,
                new WorkflowExecution { ExecutionId = "other-row", Status = WorkflowExecutionStatus.Running, UpdatedTime = stale, TenantId = tenant });
            await seed.SaveChangesAsync();
        }
        // The seed context has no filter manager, so with multi-tenancy on it cannot read a tenant
        // row back; the stamp the audit fill assigned is on the saved instance itself.
        var stamp = tenantRow.ConcurrencyStamp;
        stamp.ShouldNotBeNullOrEmpty();

        var provider = BuildProvider();
        var heartbeat = provider.GetRequiredService<IWorkflowExecutionHeartbeat>();

        await heartbeat.BeatAsync("tenant-row");

        var filterManager = new DataFilterManager();
        using var ctx = CreateContext(filterManager);
        using (filterManager.Disable<IMultiTenantFilter>())
        {
            var beaten = await ctx.Set<WorkflowExecution>().AsNoTracking().FirstAsync(e => e.ExecutionId == "tenant-row");
            beaten.UpdatedTime.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
            beaten.ConcurrencyStamp.ShouldBe(stamp, "a heartbeat must not invalidate the request scope's optimistic-concurrency token");
            beaten.Status.ShouldBe(WorkflowExecutionStatus.Running);

            var untouched = await ctx.Set<WorkflowExecution>().AsNoTracking().FirstAsync(e => e.ExecutionId == "other-row");
            untouched.UpdatedTime.ShouldBe(stale, TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void Interval_ComesFromTheWatchdogOptions()
    {
        var provider = BuildProvider(new WorkflowWatchdogOptions { HeartbeatInterval = TimeSpan.FromSeconds(7) });
        provider.GetRequiredService<IWorkflowExecutionHeartbeat>().Interval.ShouldBe(TimeSpan.FromSeconds(7));
    }

    private IServiceProvider BuildProvider(WorkflowWatchdogOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_connection);
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddScoped(sp => CreateContext(sp.GetService<IDataFilterManager>()));
        services.AddScoped<IRepository<WorkflowExecution, Guid>>(sp =>
            new EFCoreRepository<WatchdogMtDbContext, WorkflowExecution, Guid>(sp.GetRequiredService<WatchdogMtDbContext>()));
        services.AddSingleton<IOptionsMonitor<WorkflowWatchdogOptions>>(new StaticOptionsMonitor<WorkflowWatchdogOptions>(options ?? new WorkflowWatchdogOptions()));
        services.AddSingleton<ILogger<DatabaseWorkflowExecutionHeartbeat>>(NullLogger<DatabaseWorkflowExecutionHeartbeat>.Instance);
        services.AddSingleton<IWorkflowExecutionHeartbeat, DatabaseWorkflowExecutionHeartbeat>();
        return services.BuildServiceProvider();
    }

    private WatchdogMtDbContext CreateContext(IDataFilterManager? dataFilterManager)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);
        currentUser.Setup(m => m.TenantId).Returns((Guid?)null);
        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.Setup(t => t.Id).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<WatchdogMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new WatchdogMtDbContext(options, currentUser.Object, currentTenant.Object, dataFilterManager,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }
}
