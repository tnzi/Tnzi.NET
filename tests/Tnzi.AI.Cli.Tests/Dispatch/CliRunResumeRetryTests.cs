using System.Runtime.CompilerServices;
using Tnzi.Json;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 续接被拒后开新会话重试一次：重试期间运行仍是 Running，终态只在最后一次写一回。
/// </summary>
/// <remarks>
/// 此前被拒的那一次照常落终态 Failed，之后才开始重试：等结果的流式端点与门面读到终态就收尾了，
/// 重试跑出的答案没人接、不进线程却照常计费；续租停了，取消端点答 409，回写凭据作废，
/// 而新会话指针照样存成下一轮的续接点，下一轮的 agent 记得一段用户从没看到过的回答。
/// 跑的是真实的 <see cref="CliRunExecutor"/> + SQLite 仓储，只有进程与协议层是假的。
/// </remarks>
public class CliRunResumeRetryTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111a1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222a2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333a3");
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-9999999999a9");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspaces = TestScratch.NewPath("retry");

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
    public async Task ResumeRejected_TheRunStaysRunningDuringTheRetry_AndEndsWithTheRetrysAnswer()
    {
        var run = await SeedRunningResumeAsync();
        CliRun? duringRetry = null;
        var adapter = new ScriptedAdapter(onRetryStarted: async () => duringRetry = await ReloadAsync(run.Id));
        var executor = CreateExecutor(adapter, writeBack: false, out _);

        await executor.ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        duringRetry.ShouldNotBeNull();
        duringRetry.Status.ShouldBe(CliRunStatus.Running, "the rejected attempt must not write a terminal state");
        duringRetry.CompletedAt.ShouldBeNull();
        duringRetry.LeaseExpiresAt.ShouldNotBeNull("the lease is still held while the retry runs");
        duringRetry.ProviderSessionId.ShouldBeNull("the dead session pointer is dropped before the retry");

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);
        persisted.Output.ShouldBe("the answer");
        persisted.ProviderSessionId.ShouldBe("fresh-session");
        persisted.FailureReason.ShouldBeNull();

        // 两次的事件连续编号，互不冲突
        var sequences = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == run.Id).OrderBy(m => m.Sequence).Select(m => m.Sequence).ToListAsync();
        sequences.ShouldBe([1, 2, 3]);
        persisted.MessageCount.ShouldBe(3);

        adapter.Contexts.Count.ShouldBe(2);
        adapter.Contexts[0].ResumeSessionId.ShouldBe("stale-session");
        adapter.Contexts[1].ResumeSessionId.ShouldBeNull();
        adapter.Contexts[1].Prompt.ShouldContain("could not be resumed");
    }

    [Fact]
    public async Task ResumeRejected_BothAttemptsAreBilledToTheRun()
    {
        var run = await SeedRunningResumeAsync();
        var adapter = new ScriptedAdapter(
            rejectedUsage: new CliAgentTokenUsage { InputTokens = 7, OutputTokens = 0 },
            retryUsage: new CliAgentTokenUsage { InputTokens = 100, OutputTokens = 20 });
        var executor = CreateExecutor(adapter, writeBack: false, out _);

        await executor.ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        var usage = JsonSerializer.Deserialize<Dictionary<string, CliAgentTokenUsage>>(persisted.UsageJson!, TnziJsonDefaults.Options)!;
        usage["model-x"].InputTokens.ShouldBe(107);
        usage["model-x"].OutputTokens.ShouldBe(20);
    }

    [Fact]
    public async Task ResumeRejected_CancellingDuringTheRetry_EndsCancelled()
    {
        // 此前重试期间运行已是终态，取消端点答 409，重试只能跑完
        var run = await SeedRunningResumeAsync();
        using var cts = new CancellationTokenSource();
        var adapter = new ScriptedAdapter(blockRetryUntilCancelled: true);
        var executor = CreateExecutor(adapter, writeBack: false, out _);

        var execution = executor.ExecuteAsync(run.Id, cts.Token);
        await adapter.RetryStarted.Task.WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Running, "a running run is what the cancel endpoint accepts");
        await DbContext.Set<CliRun>().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));
        await cts.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Cancelled);
        persisted.CancelRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task ResumeRejected_HostShutdownDuringTheRetry_ReleasesTheRun_WithoutTheDeadSessionPointer()
    {
        var run = await SeedRunningResumeAsync();
        using var hostStopping = new CancellationTokenSource();
        var adapter = new ScriptedAdapter(blockRetryUntilCancelled: true);
        var executor = CreateExecutor(adapter, writeBack: false, out _);

        var execution = executor.ExecuteAsync(run.Id, hostStopping.Token);
        await adapter.RetryStarted.Task.WaitAsync(TestTimeout);
        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Queued);
        persisted.ProviderSessionId.ShouldBeNull("the next execution must not resume the rejected session again");
        persisted.MessageCount.ShouldBeGreaterThanOrEqualTo(1, "the next execution continues the event numbering");
    }

    [Fact]
    public async Task ResumeRejected_TheWriteBackCredentialStaysValidDuringTheRetry()
    {
        var run = await SeedRunningResumeAsync();
        RunScopedCredential? credentialDuringRetry = null;
        CliRunTokenService? tokens = null;
        Func<string?>? issuedToken = null;
        var adapter = new ScriptedAdapter(onRetryStarted: async () =>
            credentialDuringRetry = await tokens!.ValidateAsync(issuedToken!()!));
        var (executor, tokenService) = CreateExecutorWithTokens(adapter, writeBack: true, out var issued);
        tokens = tokenService;
        issuedToken = issued;

        await executor.ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        issuedToken().ShouldNotBeNull("write-back is enabled, so the run was given a credential");
        credentialDuringRetry.ShouldNotBeNull("agent write-back calls during the retry must still be accepted");
        credentialDuringRetry.RunId.ShouldBe(run.Id);
    }

    // ── 续接指针在认领之后解析 ─────────────────────────────────────────────────

    /// <summary>
    /// 前一轮还在执行时追加的下一轮，要续接前一轮结束时留下的会话。
    /// </summary>
    /// <remarks>
    /// 此前指针在入队那一刻就定下：前一轮还没结束、它的会话还不存在，下一轮拿到空指针，以全新的 CLI 会话运行，
    /// 前一轮的上下文就此丢出续接链，而且因为根本没尝试续接，不会有「历史已丢失」的说明。
    /// </remarks>
    [Fact]
    public async Task ATurnQueuedWhileThePreviousOneRan_ResumesTheSessionThatTurnLeft()
    {
        await SeedBindingAsync();
        var next = await SeedClaimedTurnAsync();
        await SeedFinishedTurnAsync("s1");
        var adapter = new AnsweringAdapter();

        await CreateExecutor(adapter, writeBack: false, out _).ExecuteAsync(next.Id, CancellationToken.None).WaitAsync(TestTimeout);

        adapter.Contexts.Single().ResumeSessionId.ShouldBe("s1");
        var persisted = await ReloadAsync(next.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);
        persisted.ResumeExpected.ShouldBeTrue();
    }

    [Fact]
    public async Task TheNewestFinishedTurn_IsTheOneResumed()
    {
        await SeedBindingAsync();
        await SeedFinishedTurnAsync("older", completedAt: DateTime.UtcNow.AddMinutes(-10));
        await SeedFinishedTurnAsync("newer", completedAt: DateTime.UtcNow.AddMinutes(-2));
        var next = await SeedClaimedTurnAsync();
        var adapter = new AnsweringAdapter();

        await CreateExecutor(adapter, writeBack: false, out _).ExecuteAsync(next.Id, CancellationToken.None).WaitAsync(TestTimeout);

        adapter.Contexts.Single().ResumeSessionId.ShouldBe("newer");
    }

    /// <summary>线程是自己的，但上面曾有别人的运行留下会话指针：那个指针不该被接上。</summary>
    [Fact]
    public async Task ASessionSomeoneElseLeftOnTheThread_IsNotResumed()
    {
        await SeedBindingAsync();
        await SeedFinishedTurnAsync("theirs", creatorId: SomeoneElse);
        var next = await SeedClaimedTurnAsync();
        var adapter = new AnsweringAdapter();

        await CreateExecutor(adapter, writeBack: false, out _).ExecuteAsync(next.Id, CancellationToken.None).WaitAsync(TestTimeout);

        adapter.Contexts.Single().ResumeSessionId.ShouldBeNull();
        (await ReloadAsync(next.Id)).ResumeExpected.ShouldBeFalse();
    }

    /// <summary>PerRun 每轮换目录，CLI 在新目录里找不到上一轮的会话存档，明知必被拒就不发 --resume。</summary>
    [Fact]
    public async Task APerRunBinding_NeverResumes()
    {
        await SeedBindingAsync(CliWorkDirectoryMode.PerRun);
        await SeedFinishedTurnAsync("s1");
        var next = await SeedClaimedTurnAsync();
        var adapter = new AnsweringAdapter();

        await CreateExecutor(adapter, writeBack: false, out _).ExecuteAsync(next.Id, CancellationToken.None).WaitAsync(TestTimeout);

        adapter.Contexts.Single().ResumeSessionId.ShouldBeNull();
    }

    /// <summary>
    /// 重新执行（停机打回队列、租约回收）沿用第一次执行定下的指针：第一次续接被拒、指针已清空，
    /// 重新执行不能再去线程上找回那个刚被拒绝的会话。
    /// </summary>
    [Fact]
    public async Task AReExecutedRun_DoesNotResumeTheSessionItsFirstExecutionHadRejected()
    {
        var run = await SeedRunningResumeAsync();
        using var hostStopping = new CancellationTokenSource();
        var first = new ScriptedAdapter(blockRetryUntilCancelled: true);
        var execution = CreateExecutor(first, writeBack: false, out _).ExecuteAsync(run.Id, hostStopping.Token);
        await first.RetryStarted.Task.WaitAsync(TestTimeout);
        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);
        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Queued);

        // 另一个副本再认领它
        await DbContext.Set<CliRun>().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Dispatched)
                .SetProperty(r => r.LeaseExpiresAt, DateTime.UtcNow.AddMinutes(5)));
        var second = new AnsweringAdapter();

        await CreateExecutor(second, writeBack: false, out _).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        second.Contexts.Single().ResumeSessionId.ShouldBeNull();
        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>一条线程上一轮留下了会话 <c>stale-session</c>，这一轮已被认领、等着执行。</summary>
    private async Task<CliRun> SeedRunningResumeAsync()
    {
        await SeedBindingAsync();
        await SeedFinishedTurnAsync("stale-session");
        return await SeedClaimedTurnAsync();
    }

    private async Task SeedBindingAsync(CliWorkDirectoryMode mode = CliWorkDirectoryMode.PerThread)
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = false,
            WorkDirectoryMode = mode
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
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>线程上一轮已经结束、留下了会话指针。派出者默认是测试用户。</summary>
    private async Task<CliRun> SeedFinishedTurnAsync(string sessionId, Guid? creatorId = null, DateTime? completedAt = null)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Completed,
            Prompt = "an earlier turn",
            ProviderSessionId = sessionId,
            CompletedAt = completedAt ?? DateTime.UtcNow.AddMinutes(-1)
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        if (creatorId is { } creator)
        {
            await DbContext.Set<CliRun>().Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatorId, creator));
        }

        DbContext.ChangeTracker.Clear();
        return run;
    }

    /// <summary>这一轮：已被认领、还没开始执行，入队时没有带任何会话指针。</summary>
    private async Task<CliRun> SeedClaimedTurnAsync()
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "what did we decide?"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private CliRunExecutor CreateExecutor(ICliProtocolAdapter adapter, bool writeBack, out Func<string?> issuedToken)
        => CreateExecutorWithTokens(adapter, writeBack, out issuedToken).Executor;

    private (CliRunExecutor Executor, CliRunTokenService Tokens) CreateExecutorWithTokens(
        ICliProtocolAdapter adapter, bool writeBack, out Func<string?> issuedToken)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var tokens = new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance);

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
        preparer.Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
        preparer.Setup(p => p.ReuseAsync(It.IsAny<ICliHostTarget>(), It.IsAny<string>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CliWorkspace?)null);

        // 回写凭据随 MCP 配置交给 agent：从组装器那里截下来，供测试拿去验证
        string? captured = null;
        var mcp = new Mock<ICliMcpConfigComposer>();
        mcp.Setup(c => c.Compose(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CliWriteBackOptions>(), It.IsAny<ICliHostTarget>()))
            .Callback<string?, string?, CliWriteBackOptions, ICliHostTarget>((_, token, _, _) => captured = token)
            .Returns((string?)null);
        issuedToken = () => captured;

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "agent" });

        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions
        {
            Enabled = true,
            WriteBack = new CliWriteBackOptions { Enabled = writeBack, McpEndpoint = "https://host/mcp/sse" }
        });

        var executor = new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            registry.Object,
            adapters.Object,
            host,
            preparer.Object,
            Mock.Of<ICliBriefComposer>(),
            mcp.Object,
            tokens,
            new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            options.Object,
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        return (executor, tokens);
    }

    /// <summary>
    /// 第一次：续接被拒（一条事件后以 ResumeRejected 收场）；第二次：新会话，两条事件后答完。
    /// </summary>
    private sealed class ScriptedAdapter(
        Func<Task>? onRetryStarted = null,
        bool blockRetryUntilCancelled = false,
        CliAgentTokenUsage? rejectedUsage = null,
        CliAgentTokenUsage? retryUsage = null) : ICliProtocolAdapter
    {
        private int _attempt;

        public List<CliAgentLaunchContext> Contexts { get; } = [];

        public TaskCompletionSource RetryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

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
            if (_attempt == 1)
            {
                yield return new CliAgentEvent { Type = CliAgentEventType.Status, Status = "resuming" };
                yield break;
            }

            yield return new CliAgentEvent { Type = CliAgentEventType.Status, Status = "running", SessionId = "fresh-session" };
            RetryStarted.TrySetResult();
            if (onRetryStarted != null)
            {
                await onRetryStarted();
            }

            if (blockRetryUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "the answer" };
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
        {
            if (_attempt == 1)
            {
                return new CliAgentResult
                {
                    Status = CliRunStatus.Failed,
                    FailureReason = CliRunFailureReason.ResumeRejected,
                    Error = "No conversation found.",
                    ResumeRejected = true,
                    DurationMs = 5,
                    Usage = rejectedUsage is null ? new Dictionary<string, CliAgentTokenUsage>() : new() { ["model-x"] = rejectedUsage }
                };
            }

            if (outcome.Cancelled)
            {
                return new CliAgentResult { Status = CliRunStatus.Cancelled, FailureReason = CliRunFailureReason.Cancelled };
            }

            return new CliAgentResult
            {
                Status = CliRunStatus.Completed,
                Output = "the answer",
                SessionId = "fresh-session",
                DurationMs = 10,
                Usage = retryUsage is null ? new Dictionary<string, CliAgentTokenUsage>() : new() { ["model-x"] = retryUsage }
            };
        }
    }

    /// <summary>每一次都正常答完、留下会话 <c>next-session</c>，并记下启动参数。</summary>
    private sealed class AnsweringAdapter : ICliProtocolAdapter
    {
        public List<CliAgentLaunchContext> Contexts { get; } = [];

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
        {
            Contexts.Add(context);
            return new CliProcessSpec { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };
        }

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport, CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "the answer", SessionId = "next-session" };
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome) => new()
        {
            Status = CliRunStatus.Completed,
            Output = "the answer",
            SessionId = "next-session",
            DurationMs = 1
        };
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
