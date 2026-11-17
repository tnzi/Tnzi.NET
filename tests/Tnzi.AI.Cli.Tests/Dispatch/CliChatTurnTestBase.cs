using System.Runtime.CompilerServices;
using Tnzi.AI.Engine;
using Tnzi.EventBus;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// The fixture of the chat-turn tests: a CLI-bound agent, a thread, a claimed run, and the real executor over a scripted adapter.
/// </summary>
/// <remarks>The database is SQLite in memory unless a derived class picks another (<see cref="IntegratedTestBase{TDbContext}.ConfigureDatabase"/>).</remarks>
public abstract class CliChatTurnTestBase : IntegratedTestBase<CliChatDbContext>
{
    protected static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111c1");
    protected static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222c2");
    protected static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    protected static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE
    ];

    protected string Workspaces { get; } = TestScratch.NewPath("chat-turn");
    protected Mock<IEventPublisher> Publisher { get; } = new();
    protected List<CliRunFinishedEvent> Finished { get; } = [];

    /// <summary>Handovers of a process to the thread's next run, in order; <see cref="OnHandedOver"/> runs as a synchronous handler.</summary>
    protected List<CliRunProcessHandedOverEvent> HandedOver { get; } = [];

    protected Action<CliRunProcessHandedOverEvent>? OnHandedOver { get; set; }

    /// <summary>The consumer's file-attachment policy, as the facade and the dispatcher see it (refuses files until a test says otherwise).</summary>
    protected TestFilePolicy FilePolicy { get; } = new();

    /// <summary>Launch-profile contributors the executor runs (none unless a test adds one).</summary>
    protected List<ICliLaunchProfileContributor> ProfileContributors { get; } = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliChatDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliChatDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliChatDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliChatDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<CliChatDbContext, Agent, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliChatDbContext, AgentThread, Guid>>();
        services.AddScoped<IRepository<AgentThreadMessage, Guid>, EFCoreRepository<CliChatDbContext, AgentThreadMessage, Guid>>();
        services.AddScoped<IRepository<AgentAttachment, Guid>, EFCoreRepository<CliChatDbContext, AgentAttachment, Guid>>();
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentThreadService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentThreadInternalService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentAttachmentService, AgentAttachmentService>();
        services.AddScoped(_ => Publisher.Object);
        services.AddScoped<ICliFileAttachmentPolicy>(_ => FilePolicy);

        // 运行与线程的 DTO 投影要 Mapster：用默认配置，与其它调度器测试同一做法
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    /// <summary>
    /// Where an executor's repositories come from: the test's own services by default (one DbContext on the shared in-memory
    /// connection). A fixture whose executions run at once on a database that takes it gives each one a scope of its own.
    /// </summary>
    protected virtual IServiceProvider ExecutorServices() => ServiceProvider;

    public override void Dispose()
    {
        TestScratch.Delete(Workspaces);
        base.Dispose();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Streams a turn through the facade, playing the queue processor's part when the run starts.</summary>
    protected async Task<List<AgentStreamChunk>> StreamThroughAsync(AgentDispatchFacade facade, ScriptedAdapter adapter, AgentRunRequest request)
    {
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in facade.RunStreamingAsync(request))
        {
            chunks.Add(chunk);
            if (chunk.EventType == RunStreamEventTypes.RunStarted)
            {
                await ClaimAsync(chunk.RunId!.Value);
                await CreateExecutor(adapter).ExecuteAsync(chunk.RunId.Value, CancellationToken.None).WaitAsync(TestTimeout);
            }
        }

        return chunks;
    }

    protected AgentDispatchFacade CreateFacade(ICliAgentTurnHandler handler)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDispatcher());
        services.AddScoped<ICliAgentDispatcher>(sp => sp.GetRequiredService<CliAgentDispatcher>());
        services.AddScoped(_ => ServiceProvider.GetRequiredService<IAgentThreadInternalService>());
        services.AddScoped(_ => handler);
        services.AddScoped<ICliFileAttachmentPolicy>(_ => FilePolicy);

        var binding = new Mock<ICliAgentBindingService>();
        binding.Setup(b => b.GetByAgentIdAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliAgentBindingDto { AgentId = AgentId, CliRuntimeId = RuntimeId, ProviderKey = "claude", AcceptedAttachments = CliAttachmentKinds.Images | CliAttachmentKinds.Documents });

        return new AgentDispatchFacade(
            Mock.Of<IAgentRuntime>(MockBehavior.Strict),
            binding.Object,
            CreateDispatcher(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentDispatchFacade>.Instance);
    }

    protected CliAgentDispatcher CreateDispatcher()
    {
        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(CliBuiltInProviders.All["claude"]);

        return new CliAgentDispatcher(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(new CliAgentOptions { Enabled = true, PollInterval = TimeSpan.FromMilliseconds(50) }),
            ServiceProvider,
            providerRegistry: registry.Object,
            attachmentService: ServiceProvider.GetRequiredService<IAgentAttachmentService>(),
            threadMessageRepository: ServiceProvider.GetRequiredService<IRepository<AgentThreadMessage, Guid>>());
    }

    protected CliRunExecutor CreateExecutor(ScriptedAdapter adapter, CliToolResultOptions? toolResults = null, bool supportsResumeAt = true)
        => CreateExecutor(
            () => adapter,
            TestCliHost.Starting(() => new FakeProcess()),
            new CliAgentOptions { Enabled = true, ToolResults = toolResults ?? new CliToolResultOptions() },
            supportsResumeAt);

    /// <summary>
    /// The executor over any adapter (a new one per run), a given host, options and handoff registry. Without a workspace
    /// preparer every run gets a fixed local workspace and no MCP config; give the real ones to see what a run is prepared with.
    /// </summary>
    private protected CliRunExecutor CreateExecutor(
        Func<ICliProtocolAdapter> adapter, TestCliHost host, CliAgentOptions options, bool supportsResumeAt = true, CliRunHandoffs? handoffs = null,
        ICliLaunchEnvironmentComposer? launchEnvironment = null, ICliWorkspacePreparer? workspacePreparer = null,
        ICliMcpConfigComposer? mcpConfigComposer = null)
    {
        var descriptor = CliBuiltInProviders.All["claude"] with { SupportsResumeAtEntry = supportsResumeAt };
        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(descriptor);

        var adapters = new Mock<ICliProtocolAdapterFactory>();
        adapters.Setup(f => f.IsImplemented(CliAgentProtocol.StreamJson)).Returns(true);
        adapters.Setup(f => f.Create(CliAgentProtocol.StreamJson)).Returns(() => adapter());

        var workspaceRoot = Path.Combine(Workspaces, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var workspace = new CliWorkspace
        {
            RootDirectory = workspaceRoot, WorkDirectory = workspaceRoot, OutputDirectory = workspaceRoot, LogDirectory = workspaceRoot
        };
        var preparer = new Mock<ICliWorkspacePreparer>();
        preparer.Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
        preparer.Setup(p => p.ReuseAsync(It.IsAny<ICliHostTarget>(), It.IsAny<string>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CliWorkspace?)null);

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(b => b.PublishAsync(It.IsAny<CliRunFinishedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CliRunFinishedEvent, CancellationToken>((e, _) => Finished.Add(e))
            .Returns(Task.CompletedTask);
        eventBus.Setup(b => b.PublishAsync(It.IsAny<CliRunProcessHandedOverEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CliRunProcessHandedOverEvent, CancellationToken>((e, _) =>
            {
                HandedOver.Add(e);
                OnHandedOver?.Invoke(e);
            })
            .Returns(Task.CompletedTask);

        var services = ExecutorServices();
        var runs = services.GetRequiredService<IRepository<CliRun, Guid>>();
        return new CliRunExecutor(
            runs,
            services.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            services.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            services.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            TestAgents.Repository(AgentId),
            registry.Object,
            adapters.Object,
            host,
            workspacePreparer ?? preparer.Object,
            Mock.Of<ICliBriefComposer>(),
            mcpConfigComposer ?? Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            launchEnvironment ?? new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            Options(options),
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            threadRepository: services.GetRequiredService<IRepository<AgentThread, Guid>>(),
            eventBus: eventBus.Object,
            profileContributors: ProfileContributors,
            handoffs: handoffs);
    }

    /// <summary>A file-attachment policy whose answer a test sets, recording what it was asked.</summary>
    protected sealed class TestFilePolicy : ICliFileAttachmentPolicy
    {
        public bool Accepts { get; set; }

        public List<CliFileAttachmentRequest> Requests { get; } = [];

        public Task<bool> AcceptsFilesAsync(CliFileAttachmentRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Accepts);
        }
    }

    protected static IOptionsMonitor<CliAgentOptions> Options(CliAgentOptions value)
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(value);
        return monitor.Object;
    }

    protected async Task SeedBindingAsync()
    {
        DbContext.Set<Agent>().Add(new Agent { Id = AgentId, Name = "cli-bound", Provider = "claude" });
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, WorkDirectoryMode = CliWorkDirectoryMode.PerThread,
            MaterializeSkills = false, InjectAgentInstructions = false
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId, HostId = "TEST-HOST", ProviderKey = "claude", Name = "claude @ TEST-HOST",
            ExecutablePath = "/nonexistent/claude", Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    protected async Task<Guid> SeedThreadAsync()
    {
        if (!await DbContext.Set<CliAgentBinding>().AnyAsync())
        {
            await SeedBindingAsync();
        }

        var thread = new AgentThread { AgentId = AgentId, Title = "Thread", LastActivityTime = DateTime.UtcNow };
        DbContext.Set<AgentThread>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return thread.Id;
    }

    protected async Task<(Guid ThreadId, Guid UserMessageId)> SeedThreadWithQuestionAsync(string question)
    {
        var threadId = await SeedThreadAsync();
        var messageId = await AddMessageAsync(threadId, MessageRole.User, question);
        return (threadId, messageId);
    }

    protected async Task<Guid> AddMessageAsync(Guid threadId, string role, string content)
    {
        using var scope = ServiceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAgentThreadInternalService>()
            .SaveMessageAsync(threadId, role, content);
    }

    protected async Task<CliRun> SeedClaimedRunAsync(Guid threadId, Guid userMessageId, string prompt = "a question")
    {
        var run = new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, UserMessageId = userMessageId,
            Status = CliRunStatus.Dispatched, ClaimedByHostId = "TEST-HOST", DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5), Prompt = prompt
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    protected async Task SeedFinishedRunAsync(Guid threadId, Guid userMessageId, string session, string entry, DateTime completedAt)
    {
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, UserMessageId = userMessageId,
            Status = CliRunStatus.Completed, Prompt = "earlier", ProviderSessionId = session, ProviderSessionEntryId = entry,
            CompletedAt = completedAt
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    protected async Task ClaimAsync(Guid runId)
        => await DbContext.Set<CliRun>().Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, CliRunStatus.Dispatched)
            .SetProperty(r => r.ClaimedByHostId, "TEST-HOST")
            .SetProperty(r => r.LeaseExpiresAt, DateTime.UtcNow.AddMinutes(5)));

    protected async Task<CliRun> ReloadRunAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    protected sealed class FixedContextHandler(string context) : ICliAgentTurnHandler
    {
        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
            => Task.FromResult(CliTurnDecision.Proceed(context));
    }

    protected sealed class RecordingHandler : ICliAgentTurnHandler
    {
        public List<CliTurnContext> Turns { get; } = [];

        public List<CliTurnQueued> Queued { get; } = [];

        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
        {
            Turns.Add(turn);
            return Task.FromResult(CliTurnDecision.Proceed());
        }

        public Task OnTurnQueuedAsync(CliTurnQueued queued, CancellationToken cancellationToken = default)
        {
            Queued.Add(queued);
            return Task.CompletedTask;
        }
    }

    protected sealed class RefusingHandler : ICliAgentTurnHandler
    {
        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
            => Task.FromResult(CliTurnDecision.Refuse("Not in this session.", "APP_RESTRICTED"));
    }

    /// <summary>Replays a fixed event script once and ends with the given status, recording what it was launched with.</summary>
    protected sealed class ScriptedAdapter(
        IReadOnlyList<CliAgentEvent> events,
        CliRunStatus status = CliRunStatus.Completed,
        string sessionId = "session-after",
        string? entryId = "entry-after",
        CliRunFailureReason? failureReason = null,
        DateTimeOffset? usageLimitResetsAt = null,
        CliCompaction? compaction = null,
        string? output = null,
        string? error = null,
        Func<int, Task>? beforeEvent = null) : ICliProtocolAdapter
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
            for (var i = 0; i < events.Count; i++)
            {
                await Task.Yield();

                // The executor asks for the next event once the previous one is fully handled (persisted, its effects written)
                if (beforeEvent is not null)
                {
                    await beforeEvent(i);
                }

                yield return events[i];
            }
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome) => new()
        {
            Status = status,
            FailureReason = status == CliRunStatus.Completed ? null
                : failureReason ?? (status == CliRunStatus.Cancelled ? CliRunFailureReason.Cancelled : CliRunFailureReason.ProviderError),
            UsageLimitResetsAt = usageLimitResetsAt,
            Output = status != CliRunStatus.Completed ? null
                : output ?? (compaction is not null ? null : string.Concat(events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content))),
            Compaction = compaction,
            Error = status == CliRunStatus.Completed ? null : error ?? "stopped",
            SessionId = sessionId,
            SessionEntryId = entryId,
            DurationMs = 1
        };
    }

    protected sealed class FakeProcess : ICliProcess
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
