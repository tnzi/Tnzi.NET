using System.Runtime.CompilerServices;
using Tnzi.Json;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 账号到了用量上限、消费方换好了另一份登录（<see cref="ICliUsageLimitHandler"/>）：同一条运行接着跑一次，
/// 续接停下的那一次的会话，只补一句说明，不重发提示。
/// </summary>
/// <remarks>
/// 跑的是真实的 <see cref="CliRunExecutor"/> + SQLite 仓储，只有进程与协议层是假的。
/// </remarks>
public class CliRunUsageLimitRetryTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111b1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222b2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333b3");
    private static readonly DateTimeOffset ResetsAt = new(2026, 10, 24, 7, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspaces = TestScratch.NewPath("limit-retry");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();
    }

    public override void Dispose()
    {
        TestScratch.Delete(_workspaces);

        base.Dispose();
    }

    [Fact]
    public async Task ASignInElsewhere_ContinuesTheStoppedSession_AndEndsWithItsAnswer()
    {
        var run = await SeedTurnAsync();
        CliRun? duringRetry = null;
        var handler = new RecordingHandler(answer: true);
        var adapter = new LimitedAdapter(Limited(sessionId: "limited-session"), Answered(),
            onAttempt: async n => { if (n == 2) duringRetry = await ReloadAsync(run.Id); });

        await CreateExecutor(adapter, handler).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);
        persisted.FailureReason.ShouldBeNull();
        persisted.Output.ShouldBe("the answer");
        persisted.UsageLimitResetsAt.ShouldBeNull();

        duringRetry.ShouldNotBeNull();
        duringRetry.Status.ShouldBe(CliRunStatus.Running, "the stopped attempt must not write a terminal state");
        duringRetry.ProviderSessionId.ShouldBe("limited-session");

        adapter.Contexts.Count.ShouldBe(2);
        adapter.Contexts[0].ResumeSessionId.ShouldBe("earlier-session");
        adapter.Contexts[0].Prompt.ShouldStartWith("what did we decide?");
        adapter.Contexts[1].ResumeSessionId.ShouldBe("limited-session", "the prompt is already in the stopped attempt's session");
        adapter.Contexts[1].ResumeAtEntryId.ShouldBeNull();
        adapter.Contexts[1].Prompt.ShouldBe(CliRunExecutor.UsageLimitContinuation);

        var context = handler.Calls.ShouldHaveSingleItem();
        context.RunId.ShouldBe(run.Id);
        context.AgentId.ShouldBe(AgentId);
        context.ThreadId.ShouldBe(ThreadId);
        context.ProviderKey.ShouldBe("claude");
        context.ResetsAt.ShouldBe(ResetsAt);
        context.Notice.ShouldBe("You've hit your session limit");

        var sequences = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == run.Id).OrderBy(m => m.Sequence).Select(m => m.Sequence).ToListAsync();
        sequences.ShouldBe([1, 2, 3], "both attempts' events are numbered in one series");
    }

    [Fact]
    public async Task BothAttempts_AreBilledToTheRun()
    {
        var run = await SeedTurnAsync();
        var adapter = new LimitedAdapter(
            Limited(usage: new CliAgentTokenUsage { InputTokens = 3 }),
            Answered(usage: new CliAgentTokenUsage { InputTokens = 100, OutputTokens = 20 }));

        await CreateExecutor(adapter, new RecordingHandler(answer: true)).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var usage = JsonSerializer.Deserialize<Dictionary<string, CliAgentTokenUsage>>((await ReloadAsync(run.Id)).UsageJson!, TnziJsonDefaults.Options)!;
        usage["model-x"].InputTokens.ShouldBe(103);
        usage["model-x"].OutputTokens.ShouldBe(20);
    }

    [Fact]
    public async Task AStopBeforeAnySession_ResendsTheTurnFromTheSameResumePoint()
    {
        var run = await SeedTurnAsync();
        var adapter = new LimitedAdapter(Limited(sessionId: null), Answered());

        await CreateExecutor(adapter, new RecordingHandler(answer: true)).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        adapter.Contexts.Count.ShouldBe(2);
        adapter.Contexts[1].ResumeSessionId.ShouldBe("earlier-session");
        adapter.Contexts[1].Prompt.ShouldStartWith("what did we decide?");
        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task NoSignInElsewhere_EndsTheRunOnTheUsageLimit(bool? answer)
    {
        var run = await SeedTurnAsync();
        var adapter = new LimitedAdapter(Limited(), Answered());
        var handler = answer is { } given ? new RecordingHandler(given) : null;

        await CreateExecutor(adapter, handler).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        persisted.UsageLimitResetsAt.ShouldBe(ResetsAt.UtcDateTime);
        adapter.Contexts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheOtherSignInAtItsLimitToo_EndsTheRun_WithoutAskingAgain()
    {
        var run = await SeedTurnAsync();
        var handler = new RecordingHandler(answer: true);
        var adapter = new LimitedAdapter(Limited(), Limited(sessionId: "limited-again"));

        await CreateExecutor(adapter, handler).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        adapter.Contexts.Count.ShouldBe(2);
        handler.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AFailingHandler_EndsTheRunOnTheUsageLimit()
    {
        var run = await SeedTurnAsync();
        var adapter = new LimitedAdapter(Limited(), Answered());

        await CreateExecutor(adapter, new RecordingHandler(answer: true, fail: true)).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        adapter.Contexts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task HostShutdownDuringTheRetry_ReleasesTheRun_OnTheStoppedAttemptsSession()
    {
        var run = await SeedTurnAsync();
        using var hostStopping = new CancellationTokenSource();
        var adapter = new LimitedAdapter(Limited(sessionId: "limited-session"), Answered(), blockSecondUntilCancelled: true);

        var execution = CreateExecutor(adapter, new RecordingHandler(answer: true)).ExecuteAsync(run.Id, hostStopping.Token);
        await adapter.SecondStarted.Task.WaitAsync(TestTimeout);
        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Queued);
        persisted.ProviderSessionId.ShouldBe("limited-session", "the next execution continues the session that holds this turn");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record Attempt(string? SessionId, CliAgentResult Result);

    private static Attempt Limited(string? sessionId = "limited-session", CliAgentTokenUsage? usage = null) => new(sessionId, new CliAgentResult
    {
        Status = CliRunStatus.Failed,
        FailureReason = CliRunFailureReason.UsageLimit,
        Error = "You've hit your session limit",
        SessionId = sessionId,
        UsageLimitResetsAt = ResetsAt,
        DurationMs = 5,
        Usage = usage is null ? new Dictionary<string, CliAgentTokenUsage>() : new() { ["model-x"] = usage }
    });

    private static Attempt Answered(CliAgentTokenUsage? usage = null) => new("answered-session", new CliAgentResult
    {
        Status = CliRunStatus.Completed,
        Output = "the answer",
        SessionId = "answered-session",
        DurationMs = 10,
        Usage = usage is null ? new Dictionary<string, CliAgentTokenUsage>() : new() { ["model-x"] = usage }
    });

    /// <summary>一条线程上一轮留下了会话 <c>earlier-session</c>，这一轮已被认领、等着执行。</summary>
    private async Task<CliRun> SeedTurnAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = false,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
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
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Completed,
            Prompt = "an earlier turn",
            ProviderSessionId = "earlier-session",
            CompletedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "what did we decide?",
            PerTurnContext = "It is Saturday."
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private CliRunExecutor CreateExecutor(ICliProtocolAdapter adapter, ICliUsageLimitHandler? handler)
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
        preparer.Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
        preparer.Setup(p => p.ReuseAsync(It.IsAny<ICliHostTarget>(), It.IsAny<string>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CliWorkspace?)null);

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "agent" });

        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });

        return new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            registry.Object,
            adapters.Object,
            TestCliHost.Starting(() => new FakeProcess()),
            preparer.Object,
            Mock.Of<ICliBriefComposer>(),
            Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            options.Object,
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            usageLimitHandler: handler);
    }

    private sealed class RecordingHandler(bool answer, bool fail = false) : ICliUsageLimitHandler
    {
        public List<CliUsageLimitContext> Calls { get; } = [];

        public Task<bool> OnUsageLimitAsync(CliUsageLimitContext context, CancellationToken cancellationToken)
        {
            Calls.Add(context);
            return fail ? throw new InvalidOperationException("the store is down") : Task.FromResult(answer);
        }
    }

    /// <summary>每一次执行照脚本走：一条带会话 id 的状态事件，然后给出那一次的结果。</summary>
    private sealed class LimitedAdapter(
        Attempt first, Attempt second, Func<int, Task>? onAttempt = null, bool blockSecondUntilCancelled = false) : ICliProtocolAdapter
    {
        private int _attempt;

        public List<CliAgentLaunchContext> Contexts { get; } = [];

        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        private Attempt Current => _attempt == 1 ? first : second;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
        {
            Contexts.Add(context);
            return new CliProcessSpec { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };
        }

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport, CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _attempt++;
            yield return new CliAgentEvent { Type = CliAgentEventType.Status, Status = "running", SessionId = Current.SessionId };
            if (onAttempt != null)
            {
                await onAttempt(_attempt);
            }

            if (_attempt == 2)
            {
                SecondStarted.TrySetResult();
                if (blockSecondUntilCancelled)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "the answer" };
            }
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => outcome.Cancelled
                ? new CliAgentResult { Status = CliRunStatus.Cancelled, FailureReason = CliRunFailureReason.Cancelled }
                : Current.Result;
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
