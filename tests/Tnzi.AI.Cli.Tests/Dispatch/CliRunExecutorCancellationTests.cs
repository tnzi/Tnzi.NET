using System.Runtime.CompilerServices;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 取消一条<b>正在跑</b>的运行必须让它停在 Cancelled，而不是被打回队列再跑一遍。
/// </summary>
/// <remarks>
/// <para>
/// 用户取消与宿主停机走的是同一枚令牌（登记处把运行令牌链到宿主的 stoppingToken 上），
/// 执行器只凭令牌分不清两者。此前的形态：进程被杀之后 <c>CompleteAsync</c> 仍带着已取消的令牌
/// 去写库，写库抛 OCE，掉进「宿主停机 → 打回队列」分支，整实体回写又把内存里过期的
/// <c>CancelRequested=false</c> 盖回数据库 —— 下一轮认领谓词 <c>Queued &amp;&amp; !CancelRequested</c>
/// 命中，同一条运行从头再跑一次、再付一次钱，而这一次已经没有人能取消它。
/// </para>
/// <para>
/// 这组用例跑的是真实的 <see cref="CliRunExecutor"/> + SQLite 仓储；只有进程与协议层是假的
/// （一个把「运行中」阻塞到令牌被取消为止的适配器）。
/// </para>
/// </remarks>
public class CliRunExecutorCancellationTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspaces = TestScratch.NewPath("cancel");

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
    public async Task Cancel_WhileRunning_EndsCancelled_KeepsCancelRequested_AndIsNotReclaimed()
    {
        var run = await SeedClaimedRunAsync();
        var adapter = new BlockingAdapter();
        var executor = CreateExecutor(adapter);
        using var cts = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(run.Id, cts.Token);
        await adapter.Started.Task.WaitAsync(TestTimeout);

        // 与 CliAgentDispatcher.CancelAsync 同序：先写库（跨副本的权威事实），再取消本地令牌。
        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));
        await cts.CancelAsync();

        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Cancelled);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        persisted.CancelRequested.ShouldBeTrue("the cancel flag is the user's decision; a full-entity write must not overwrite it");
        persisted.CompletedAt.ShouldNotBeNull();
        persisted.LeaseExpiresAt.ShouldBeNull();

        // 终态的运行不会再被任何副本认领。
        (await CreateProcessor().TryClaimAsync(CancellationToken.None)).ShouldBeNull();
        adapter.Runs.ShouldBe(1, "a cancelled run must never be executed a second time");
    }

    [Fact]
    public async Task HostShutdown_WhileRunning_ReleasesToQueue_WithoutTouchingCancelRequested()
    {
        var run = await SeedClaimedRunAsync();
        var adapter = new BlockingAdapter();
        var executor = CreateExecutor(adapter);
        using var hostStopping = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(run.Id, hostStopping.Token);
        await adapter.Started.Task.WaitAsync(TestTimeout);

        // 没有人写过 CancelRequested：令牌被取消只能是宿主在停机。
        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Queued);
        persisted.CancelRequested.ShouldBeFalse();
        persisted.ClaimedByHostId.ShouldBeNull();
        persisted.LeaseExpiresAt.ShouldBeNull();
        persisted.CompletedAt.ShouldBeNull();

        // 打回队列的运行必须还能被认领：这是「停机不丢任务」的另一半。
        (await CreateProcessor().TryClaimAsync(CancellationToken.None)).ShouldBe(run.Id);
    }

    /// <summary>
    /// 打回队列的运行要能<b>跑完</b>，而不只是能被认领：第一次已经落了事件，重新执行从那之后接着编号。
    /// </summary>
    /// <remarks>
    /// 此前重新执行从内存里的 <c>MessageCount</c>（只在终态时写，打回队列时还是 0）起编号，
    /// 第一条事件就撞上 (RunId, Sequence) 唯一索引，运行以 Failed/Unknown 结束：停机打回与租约回收这两条
    /// 恢复路径在「第一次至少落过一条事件」的常态下从来没有成功过，而 CLI 已经又被拉起并计费了一次。
    /// </remarks>
    [Fact]
    public async Task HostShutdown_ThenReExecution_ContinuesTheEventNumbering_AndCompletes()
    {
        var run = await SeedClaimedRunAsync();
        var blocking = new BlockingAdapter();
        using var hostStopping = new CancellationTokenSource();

        var execution = CreateExecutor(blocking).ExecuteAsync(run.Id, hostStopping.Token);
        await blocking.Started.Task.WaitAsync(TestTimeout);
        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);
        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Queued);

        (await CreateProcessor().TryClaimAsync(CancellationToken.None)).ShouldBe(run.Id);
        await CreateExecutor(new CompletingAdapter("second-a", "second-b"))
            .ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed, persisted.Error);
        persisted.MessageCount.ShouldBe(3);
        (await PersistedSequencesAsync(run.Id)).ShouldBe([1, 2, 3]);
    }

    /// <summary>宿主崩溃后的租约回收是另一条恢复路径，形状相同：事件已经落了，进程与宿主一起没了。</summary>
    [Fact]
    public async Task LeaseReclaim_ThenReExecution_ContinuesTheEventNumbering_AndCompletes()
    {
        var run = await SeedClaimedRunAsync();
        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Running)
                .SetProperty(r => r.LeaseExpiresAt, DateTime.UtcNow.AddMinutes(-5)));
        DbContext.Set<CliRunMessage>().AddRange(
            new CliRunMessage { RunId = run.Id, Sequence = 1, Type = CliAgentEventType.Status, Content = "init" },
            new CliRunMessage { RunId = run.Id, Sequence = 2, Type = CliAgentEventType.Text, Content = "half an answer" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var processor = CreateProcessor();
        await processor.ReclaimExpiredLeasesAsync(CancellationToken.None);
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBe(run.Id);

        await CreateExecutor(new CompletingAdapter("again"))
            .ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed, persisted.Error);
        persisted.MessageCount.ShouldBe(3);
        (await PersistedSequencesAsync(run.Id)).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Cancel_BeforeProcessStarts_EndsCancelled_NotQueued()
    {
        // 令牌在进程起来之前就被取消（取消恰好落在布置工作区的那几毫秒里）：
        // 这条路径上 RunProcessAsync 根本没跑到，终态必须由 ExecuteAsync 的取消分支自己写。
        var run = await SeedClaimedRunAsync();
        var adapter = new BlockingAdapter();
        using var cts = new CancellationTokenSource();
        var executor = CreateExecutor(adapter, beforeProcessStart: () => cts.Cancel());

        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));

        await executor.ExecuteAsync(run.Id, cts.Token).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Cancelled);
        persisted.CancelRequested.ShouldBeTrue();
        persisted.CompletedAt.ShouldNotBeNull();
        adapter.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task Acp_CancelWhileThePromptIsPending_EndsCancelled_NotCompletedWithHalfAnAnswer()
    {
        // 真实的 ACP 适配器：此前它吞掉取消、正常收尾，用户取消的运行落成 Completed，
        // 已收到的半句话当成交付物，CancelRequested 被整实体回写盖回 false。
        var run = await SeedClaimedRunAsync();
        var acp = new AcpRuntimeScript();
        var executor = CreateExecutor(new AcpAdapter(NullLogger<AcpAdapter>.Instance), CliAgentProtocol.Acp, acp.CreateTransport);
        using var cts = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(run.Id, cts.Token);
        await acp.PromptSent.Task.WaitAsync(TestTimeout);
        await WaitForPersistedTextAsync(run.Id, "The answer is");

        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));
        await cts.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Cancelled);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.Cancelled);
        persisted.Output.ShouldBeNull("a cut-off answer is not a deliverable");
        persisted.CancelRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Acp_HostShutdownWhileThePromptIsPending_ReleasesTheRunToTheQueue()
    {
        var run = await SeedClaimedRunAsync();
        var acp = new AcpRuntimeScript();
        var executor = CreateExecutor(new AcpAdapter(NullLogger<AcpAdapter>.Instance), CliAgentProtocol.Acp, acp.CreateTransport);
        using var hostStopping = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(run.Id, hostStopping.Token);
        await acp.PromptSent.Task.WaitAsync(TestTimeout);
        await WaitForPersistedTextAsync(run.Id, "The answer is");

        await hostStopping.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Queued, "an interrupted run goes back to the queue, it did not complete");
        persisted.CompletedAt.ShouldBeNull();
        persisted.Output.ShouldBeNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<CliRun> SeedClaimedRunAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = false
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

        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "do the thing"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        // 执行器自己按 Id 加载；把种子实体从跟踪器里摘掉，免得两份同 Id 的实例撞在一起。
        DbContext.ChangeTracker.Clear();
        return run;
    }

    /// <summary>
    /// 等到这段文本已经落库：取消要落在「事件都已写完、prompt 还没答」的那一刻，
    /// 否则一条在取消之后才写库的事件会带着已取消的令牌抛出，把缺陷掩盖掉。
    /// </summary>
    private async Task WaitForPersistedTextAsync(Guid runId, string text)
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
                    .AnyAsync(m => m.RunId == runId && m.Content == text))
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"The event '{text}' was never persisted.");
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private async Task<List<int>> PersistedSequencesAsync(Guid runId)
        => await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == runId).OrderBy(m => m.Sequence).Select(m => m.Sequence).ToListAsync();

    private CliRunQueueProcessor CreateProcessor()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            NullLogger<CliRunQueueProcessor>.Instance);

    private CliRunExecutor CreateExecutor(BlockingAdapter adapter, Action? beforeProcessStart = null)
        => CreateExecutor(adapter, CliAgentProtocol.StreamJson, transportFactory: null, beforeProcessStart);

    private CliRunExecutor CreateExecutor(CompletingAdapter adapter)
        => CreateExecutor(adapter, CliAgentProtocol.StreamJson, transportFactory: null);

    private CliRunExecutor CreateExecutor(
        ICliProtocolAdapter adapter, CliAgentProtocol protocol, Func<ICliAgentTransport>? transportFactory,
        Action? beforeProcessStart = null)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var descriptor = new CliProviderDescriptor
        {
            Key = "claude",
            DisplayName = "Claude Code",
            Protocol = protocol,
            DefaultExecutable = "claude"
        };

        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(descriptor);

        var adapters = new Mock<ICliProtocolAdapterFactory>();
        adapters.Setup(f => f.IsImplemented(protocol)).Returns(true);
        adapters.Setup(f => f.Create(protocol)).Returns(adapter);

        var host = new TestCliHost((_, ct) =>
        {
            beforeProcessStart?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<ICliProcess>(new FakeProcess(transportFactory?.Invoke() ?? new FakeCliAgentTransport([])));
        });

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

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents
            .Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "agent" });

        return new CliRunExecutor(
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
            Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            EnabledOptions(),
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    /// <summary>发一条事件后一直「运行中」，直到令牌被取消。</summary>
    private sealed class BlockingAdapter : ICliProtocolAdapter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Runs { get; private set; }

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Runs++;
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "working", SessionId = "sess-1" };
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => outcome.Cancelled
                ? new CliAgentResult
                {
                    Status = CliRunStatus.Cancelled,
                    FailureReason = CliRunFailureReason.Cancelled,
                    Error = "Execution cancelled.",
                    SessionId = "sess-1",
                    DurationMs = (long)outcome.Elapsed.TotalMilliseconds
                }
                : new CliAgentResult
                {
                    Status = CliRunStatus.Completed,
                    SessionId = "sess-1",
                    DurationMs = (long)outcome.Elapsed.TotalMilliseconds
                };
    }

    /// <summary>按给定的文本逐条发事件，然后正常结束。</summary>
    private sealed class CompletingAdapter(params string[] texts) : ICliProtocolAdapter
    {
        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var text in texts)
            {
                await Task.Yield();
                yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = text, SessionId = "sess-2" };
            }
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => new()
            {
                Status = CliRunStatus.Completed,
                Output = string.Join(" ", texts),
                SessionId = "sess-2",
                DurationMs = (long)outcome.Elapsed.TotalMilliseconds
            };
    }

    /// <summary>
    /// 一个最小的 ACP 运行时：握手照答、发出一小段回答，然后在 <c>session/prompt</c> 上一直不回（还在干活）。
    /// </summary>
    private sealed class AcpRuntimeScript
    {
        public TaskCompletionSource PromptSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ICliAgentTransport CreateTransport() => new FakeCliAgentTransport([], onWrite: Respond);

        private IEnumerable<string> Respond(string line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var method) || !root.TryGetProperty("id", out var idElement))
            {
                return [];
            }

            var id = idElement.GetRawText();
            switch (method.GetString())
            {
                case "initialize":
                    return [$"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"agentCapabilities\":{{}}}}}}"];
                case "session/new":
                    return [$"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"sessionId\":\"acp-1\"}}}}"];
                case "session/prompt":
                    PromptSent.TrySetResult();
                    return ["""{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"The answer is"}}}}"""];
                default:
                    return [];
            }
        }
    }

    private sealed class FakeProcess(ICliAgentTransport transport) : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = transport;

        public int ProcessId => 4242;

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 137 : null;

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            HasExited = true;
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(137);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
