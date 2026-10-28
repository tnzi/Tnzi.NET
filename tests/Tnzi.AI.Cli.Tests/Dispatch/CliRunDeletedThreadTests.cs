using System.Runtime.CompilerServices;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 线程被删除时还在执行（或之后才被认领）的运行，由执行器在收尾时删掉：运行记录、事件流与工作区。
/// </summary>
/// <remarks>
/// <para>
/// 线程删除的处理器删不得执行中的运行记录（续租看到「行没了」只会默默退出、不去杀进程，终态回写也会失败），
/// 它只请求取消并删掉已经写下的事件。于是收尾这一半必须由执行器做，否则那条运行的提示词、交付物与取消之前写下的最后几条事件
/// 在线程删除之后一直留着。
/// </para>
/// <para>
/// 跑的是真实的 <see cref="CliRunExecutor"/> + SQLite 仓储 + 真实的删除处理器；只有进程与协议层是假的。
/// </para>
/// </remarks>
public class CliRunDeletedThreadTests : IntegratedTestBase<CliThreadOwnershipDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RuntimeId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private readonly Mock<ICliWorkspacePreparer> _preparer = new();
    private readonly RecordingUsageLog _usage = new();
    private readonly string _workspaces = TestScratch.NewPath("deleted-thread");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, AgentThread, Guid>>();
    }

    public override void Dispose()
    {
        TestScratch.Delete(_workspaces);

        base.Dispose();
    }

    [Fact]
    public async Task ThreadDeletedWhileRunning_TheRunItsEventsAndItsWorkspaceAreRemovedWhenItStops()
    {
        var thread = await SeedThreadAsync();
        var run = await SeedClaimedRunAsync(thread);
        var adapter = new BlockingAdapter();
        using var executing = new CancellationTokenSource();
        var execution = CreateExecutor(adapter).ExecuteAsync(run, executing.Token);
        await adapter.Started.Task.WaitAsync(TestTimeout);

        // 线程删除：软删线程，然后由删除事件的处理器做它那一半
        await SoftDeleteThreadAsync(thread);
        await HandleThreadDeletedAsync(thread);
        (await RunExistsAsync(run)).ShouldBeTrue("the handler leaves an executing run's record to the executor");

        // 处理器之后、进程被杀之前又落了一条事件
        await AddEventAsync(run, sequence: 50, "written after the handler purged the events");

        // 处理器写下的取消请求由续租（或本副本的登记处）送到执行器
        await executing.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        (await RunExistsAsync(run)).ShouldBeFalse();
        (await EventCountAsync(run)).ShouldBe(0);
        _preparer.Verify(p => p.CleanupAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliWorkspace>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOfAnAlreadyDeletedThread_IsDiscardedWithoutRunning()
    {
        // 删除之后才被认领（入队与删除赛跑，或处理器没送达）：一次都不跑
        var thread = await SeedThreadAsync();
        var run = await SeedClaimedRunAsync(thread);
        await AddEventAsync(run, sequence: 1, "from an earlier attempt");
        await SoftDeleteThreadAsync(thread);
        var adapter = new CompletingAdapter("should never run");

        await CreateExecutor(adapter).ExecuteAsync(run, CancellationToken.None).WaitAsync(TestTimeout);

        adapter.Runs.ShouldBe(0);
        (await RunExistsAsync(run)).ShouldBeFalse();
        (await EventCountAsync(run)).ShouldBe(0);
    }

    [Fact]
    public async Task ThreadDeletedBeforeTheRunCompletes_UsageIsStillLogged_ButNotAgainstTheThread()
    {
        // 花费是真实发生的，照记；只是不再挂在已删除的线程上（与核心删除线程时对既有用量行的处理一致）
        var thread = await SeedThreadAsync();
        var run = await SeedClaimedRunAsync(thread);
        var adapter = new CompletingAdapter("answer") { DuringRun = () => SoftDeleteThreadAsync(thread) };

        await CreateExecutor(adapter).ExecuteAsync(run, CancellationToken.None).WaitAsync(TestTimeout);

        _usage.ThreadIds.ShouldHaveSingleItem().ShouldBeNull();
        (await RunExistsAsync(run)).ShouldBeFalse();
    }

    [Fact]
    public async Task RunOfALiveThread_IsKept_AndItsUsageStaysOnTheThread()
    {
        var thread = await SeedThreadAsync();
        var run = await SeedClaimedRunAsync(thread);

        await CreateExecutor(new CompletingAdapter("answer")).ExecuteAsync(run, CancellationToken.None).WaitAsync(TestTimeout);

        (await RunExistsAsync(run)).ShouldBeTrue();
        (await EventCountAsync(run)).ShouldBe(1);
        _usage.ThreadIds.ShouldHaveSingleItem().ShouldBe(thread);
        _preparer.Verify(p => p.CleanupAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliWorkspace>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 使用方给出配置目录的那一档：线程在运行期间被删除，这一轮跑出的会话在 CLI 配置目录里的记录也由执行器删掉
    ///（删除事件的处理器只删得了已经结束的运行的），同一项目里别的会话不动。
    /// </summary>
    [Fact]
    public async Task ThreadDeletedWhileRunning_ProvidedConfigDirectory_TheRunsSessionRecordsAreRemoved()
    {
        var thread = await SeedThreadAsync();
        var run = await SeedClaimedRunAsync(thread);
        var host = TestCliHost.Starting(() => new FakeProcess(new FakeCliAgentTransport([])));
        host.LaunchProfile = _ => new CliLaunchProfile { ConfigDirectory = "/home/u/.claude", ProjectDirectoryName = "demo" };
        await host.FileSystem.CreateDirectoryAsync("/home/u/.claude/projects/demo", CancellationToken.None);
        await host.FileSystem.WriteAllTextAsync("/home/u/.claude/projects/demo/sess-2.jsonl", "{}", CancellationToken.None);
        await host.FileSystem.WriteAllTextAsync("/home/u/.claude/projects/demo/other.jsonl", "{}", CancellationToken.None);
        var adapter = new CompletingAdapter("answer") { DuringRun = () => SoftDeleteThreadAsync(thread) };
        var provider = CliBuiltInProviders.All["claude"] with { UserConfigIsolation = CliUserConfigIsolation.ProvidedConfigDirectory };

        await CreateExecutor(adapter, provider, host).ExecuteAsync(run, CancellationToken.None).WaitAsync(TestTimeout);

        var files = ((InMemoryHostFileSystem)host.FileSystem).Files;
        files.ShouldNotContain("/home/u/.claude/projects/demo/sess-2.jsonl");
        files.ShouldContain("/home/u/.claude/projects/demo/other.jsonl");
        (await RunExistsAsync(run)).ShouldBeFalse();
    }

    [Fact]
    public async Task ThreadThatCannotBeFound_IsNotTreatedAsDeleted()
    {
        // 找不到线程不是「已删除」的证据：据此删运行记录只会删错
        var run = await SeedClaimedRunAsync(Guid.NewGuid());

        await CreateExecutor(new CompletingAdapter("answer")).ExecuteAsync(run, CancellationToken.None).WaitAsync(TestTimeout);

        (await RunExistsAsync(run)).ShouldBeTrue();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedThreadAsync()
    {
        var thread = new AgentThread { Title = "conversation" };
        DbContext.Set<AgentThread>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return thread.Id;
    }

    private async Task SoftDeleteThreadAsync(Guid threadId)
    {
        using var scope = ServiceProvider.CreateScope();
        var threads = scope.ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>();
        await threads.AsQueryable().IgnoreQueryFilters().Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
    }

    private async Task HandleThreadDeletedAsync(Guid threadId)
    {
        using var scope = ServiceProvider.CreateScope();
        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = Path.Combine(_workspaces, Guid.NewGuid().ToString("N"))
        });

        await new CliThreadCleanupHandler(
                scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
                scope.ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
                new CliRunCancellationRegistry(),
                new CliRunSignalHub(),
                options.Object,
                NullLogger<CliThreadCleanupHandler>.Instance)
            .HandleAsync(new ThreadDeletedEvent { ThreadId = threadId });
    }

    private async Task<Guid> SeedClaimedRunAsync(Guid threadId)
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
            ThreadId = threadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "read my files and summarize them"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run.Id;
    }

    private async Task AddEventAsync(Guid runId, int sequence, string content)
    {
        DbContext.Set<CliRunMessage>().Add(new CliRunMessage
        {
            RunId = runId,
            Sequence = sequence,
            Type = CliAgentEventType.ToolResult,
            Output = content
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<bool> RunExistsAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().AnyAsync(r => r.Id == runId);

    private async Task<int> EventCountAsync(Guid runId)
        => await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters().CountAsync(m => m.RunId == runId);

    private CliRunExecutor CreateExecutor(ICliProtocolAdapter adapter, CliProviderDescriptor? provider = null, TestCliHost? testHost = null)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var descriptor = provider ?? new CliProviderDescriptor
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

        var host = testHost ?? TestCliHost.Starting(() => new FakeProcess(new FakeCliAgentTransport([])));

        var workspaceRoot = Path.Combine(_workspaces, Guid.NewGuid().ToString("N"));
        var workspace = new CliWorkspace
        {
            RootDirectory = workspaceRoot,
            WorkDirectory = workspaceRoot,
            OutputDirectory = workspaceRoot,
            LogDirectory = workspaceRoot
        };
        _preparer
            .Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspace);

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
            _preparer.Object,
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
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            usageLogService: _usage,
            threadRepository: ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>());
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    private static readonly IReadOnlyDictionary<string, CliAgentTokenUsage> OneModelUsage =
        new Dictionary<string, CliAgentTokenUsage> { ["model-a"] = new() { InputTokens = 100, OutputTokens = 20 } };

    /// <summary>发一条事件后一直「运行中」，直到令牌被取消。</summary>
    private sealed class BlockingAdapter : ICliProtocolAdapter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "working", SessionId = "sess-1" };
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => new()
            {
                Status = outcome.Cancelled ? CliRunStatus.Cancelled : CliRunStatus.Completed,
                FailureReason = outcome.Cancelled ? CliRunFailureReason.Cancelled : null,
                SessionId = "sess-1",
                Usage = OneModelUsage,
                DurationMs = (long)outcome.Elapsed.TotalMilliseconds
            };
    }

    /// <summary>按给定的文本逐条发事件，然后正常结束；可在两条事件之间做点什么（例如删掉线程）。</summary>
    private sealed class CompletingAdapter(params string[] texts) : ICliProtocolAdapter
    {
        public int Runs { get; private set; }

        public Func<Task>? DuringRun { get; init; }

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Runs++;
            foreach (var text in texts)
            {
                await Task.Yield();
                yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = text, SessionId = "sess-2" };
            }

            if (DuringRun is not null)
            {
                await DuringRun();
            }
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome)
            => new()
            {
                Status = CliRunStatus.Completed,
                Output = string.Join(" ", texts),
                SessionId = "sess-2",
                Usage = OneModelUsage,
                DurationMs = (long)outcome.Elapsed.TotalMilliseconds
            };
    }

    /// <summary>记下每一行用量挂在哪条线程上。</summary>
    private sealed class RecordingUsageLog : IUsageLogService
    {
        public List<Guid?> ThreadIds { get; } = [];

        public Task LogUsageAsync(
            string operationType, string provider, string model, int inputTokens, int outputTokens, long durationMs,
            bool isSuccess, string? errorMessage = null, Guid? agentId = null, Guid? threadId = null,
            CancellationToken ct = default)
        {
            ThreadIds.Add(threadId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcess(ICliAgentTransport transport) : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = transport;

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
