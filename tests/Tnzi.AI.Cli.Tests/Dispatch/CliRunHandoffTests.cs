namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 一条运行在等后台任务（开发服务器）时，同线程的下一条消息不排在等待后面：它接过那个进程，后台任务照常跑（文档 §6.18）。
/// </summary>
/// <remarks>
/// 真实的 stream-json 适配器读一个可交互的假进程：stdout 按测试的脚本给出，写进 stdin 的提示由脚本应答成下一轮。
/// 队列处理器的认领（<see cref="CliRunQueueProcessor.TryClaimContinuationAsync"/>）与两条运行的执行器都是真的，共用一个交接处。
/// </remarks>
public class CliRunHandoffTests : CliChatTurnTestBase
{
    private const string Session = "s-dev";

    private readonly CliRunHandoffs _handoffs = new();

    private readonly string _databaseDirectory = TestScratch.CreateDirectory("handoff-db");

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Two executions run at once (the waiting run winds down while the next one answers on its process) beside the
        // test's own reads: the base's one shared in-memory connection does not stand that (SQLite Error 5, database is
        // locked, or a write lost under it). A file database with a connection per scope queues the writers on SQLite's busy wait
        services.RemoveAll<DbContextOptions<CliChatDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<CliChatDbContext>>();
        services.AddDbContext<CliChatDbContext>(options => options
            .UseSqlite($"Data Source={Path.Combine(_databaseDirectory, "handoff.sqlite")};Pooling=False")
            .EnableSensitiveDataLogging());
    }

    private readonly List<IServiceScope> _executorScopes = [];

    /// <summary>
    /// A scope per execution, as the queue processor gives each run: the waiting run and the one taking its process over
    /// must not share one DbContext, which EF does not allow and the production code never does.
    /// </summary>
    protected override IServiceProvider ExecutorServices()
    {
        var scope = ServiceProvider.CreateScope();
        _executorScopes.Add(scope);
        return scope.ServiceProvider;
    }

    public override void Dispose()
    {
        foreach (var scope in _executorScopes)
        {
            scope.Dispose();
        }

        base.Dispose();
        TestScratch.Delete(_databaseDirectory);
    }

    // ---------------------------------------------------------------- frames (the shapes of claude 2.1.317's stream-json)

    private static string Init() => JsonSerializer.Serialize(new { type = "system", subtype = "init", session_id = Session });

    private static string Tasks(params string[] ids) => JsonSerializer.Serialize(new
    {
        type = "system", subtype = "background_tasks_changed", session_id = Session,
        tasks = ids.Select(id => new { task_id = id, task_type = "local_bash", description = "npm run dev" })
    });

    private static string Text(string id, string text) => JsonSerializer.Serialize(new
    {
        type = "assistant", uuid = $"entry-{id}", session_id = Session,
        message = new { model = "m", id, role = "assistant", content = new[] { new { type = "text", text } } }
    });

    private static string Result(string text, string? origin = null)
    {
        var frame = new JsonObject
        {
            ["type"] = "result", ["subtype"] = "success", ["result"] = text, ["session_id"] = Session, ["num_turns"] = 1,
            ["modelUsage"] = new JsonObject { ["m"] = new JsonObject { ["inputTokens"] = 1, ["outputTokens"] = 1, ["costUSD"] = 0.01 } }
        };
        if (origin is not null)
        {
            frame["origin"] = new JsonObject { ["kind"] = origin };
        }

        return frame.ToJsonString();
    }

    private static string[] TaskFinished() =>
    [
        JsonSerializer.Serialize(new { type = "system", subtype = "task_notification", task_id = "srv", status = "completed", summary = "Background command \"npm run dev\" completed (exit code 0)", session_id = Session }),
        Tasks()
    ];

    // ---------------------------------------------------------------- the process

    /// <summary>
    /// A CLI process: the first prompt starts a dev server in the background and answers; each later prompt is answered
    /// by <c>answer</c> (the server keeps running). Terminating it ends its output, as a killed process does.
    /// </summary>
    private sealed class DevServerProcess : ICliProcess
    {
        private readonly FakeCliAgentTransport _transport;

        public DevServerProcess(Func<string, string?> answer)
        {
            _transport = new FakeCliAgentTransport([], onWrite: line =>
            {
                Prompts.Add(line);
                OnPrompt?.Invoke(line);
                if (Prompts.Count == 1)
                {
                    return [Init(), Tasks("srv"), Text("msg_1", "The dev server is running."), Result("The dev server is running.")];
                }

                return answer(line) is { } text
                    ? [Init(), Text($"msg_{Prompts.Count}", text), Result(text)]
                    : [];
            });
        }

        public List<string> Prompts { get; } = [];

        /// <summary>Runs when a prompt reaches the process, before it answers (the moment its next model call is made).</summary>
        public Action<string>? OnPrompt { get; set; }

        public FakeCliAgentTransport Output => _transport;

        public ICliAgentTransport Transport => _transport;

        public int ProcessId => 4242;

        public bool Terminated { get; private set; }

        public bool HasExited => Terminated || _transport.InputClosed;

        public int? ExitCode => HasExited ? 0 : null;

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            Terminated = true;
            _transport.CompleteOutput();
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ---------------------------------------------------------------- helpers

    private CliRunExecutor Executor(TestCliHost host, TimeSpan? wait = null, bool writeBack = false)
        => CreateExecutor(
            () => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance),
            host,
            new CliAgentOptions
            {
                Enabled = true,
                BackgroundTaskWait = wait ?? TimeSpan.FromMinutes(30),
                WriteBack = new CliWriteBackOptions { Enabled = writeBack }
            },
            handoffs: _handoffs);

    private CliRunQueueProcessor Processor() => new(
        ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
        new CliRunSignalHub(),
        new CliRunCancellationRegistry(),
        Options(new CliAgentOptions { Enabled = true }),
        NullLogger<CliRunQueueProcessor>.Instance,
        handoffs: _handoffs);

    private async Task<Guid> EnqueueAsync(Guid threadId, string question, string? model = null)
    {
        var messageId = await AddMessageAsync(threadId, MessageRole.User, question);
        var run = new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, UserMessageId = messageId,
            Status = CliRunStatus.Queued, Prompt = question, Model = model
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run.Id;
    }

    private async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        while (!await condition())
        {
            if (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting until {what}.");
            }

            await Task.Delay(20, CancellationToken.None);
        }
    }

    private Task WaitingAsync(Guid runId)
        => WaitUntilAsync(() => Task.FromResult(_handoffs.WaitingRuns().Any(w => w.RunId == runId)), $"run {runId} waits on its background tasks");

    private async Task<List<string>> RepliesAsync(Guid threadId)
        => await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.Order)
            .Select(m => m.Role + ": " + m.Content)
            .ToListAsync();

    // ---------------------------------------------------------------- tests

    /// <summary>
    /// The real case: the assistant started the site's dev server in the background and answered. The next message does
    /// not wait for the server to stop: the waiting run ends at its answer, the next run writes its prompt into the same
    /// process and its answer is in the thread at once, while the server keeps running (the process is never ended,
    /// no second process is started). When the server later stops, the CLI's own answer about it is a reply of its own,
    /// after the user's turn.
    /// </summary>
    [Fact]
    public async Task ANextMessageWhileWaiting_IsAnsweredOnTheSameProcess_AndTheServerKeepsRunning()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(prompt => prompt.Contains("what port") ? "It listens on 20240." : null);
        var host = TestCliHost.Starting(() => process);

        var firstExecution = Executor(host, writeBack: true).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var issuedHash = (await ReloadRunAsync(first.Id)).WriteBackTokenHash.ShouldNotBeNull();

        var second = await EnqueueAsync(threadId, "what port is it on?");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);

        var secondExecution = Executor(host, writeBack: true).ExecuteAsync(second, CancellationToken.None);
        await firstExecution.WaitAsync(TestTimeout);
        await WaitingAsync(second);

        // The waiting run ended at its answer; the next one answered on the same process
        var firstRun = await ReloadRunAsync(first.Id);
        firstRun.Status.ShouldBe(CliRunStatus.Completed, firstRun.Error);
        firstRun.Output.ShouldBe("The dev server is running.");
        firstRun.ProviderSessionId.ShouldBe(Session);
        firstRun.WriteBackTokenHash.ShouldBeNull();

        var secondRun = await ReloadRunAsync(second);
        secondRun.Status.ShouldBe(CliRunStatus.Running, "it waits on the server in turn");
        secondRun.ProviderSessionId.ShouldBe(Session);
        secondRun.WriteBackTokenHash.ShouldBe(issuedHash, "the process's write-back credential now counts as the next run's");
        (await RepliesAsync(threadId)).ShouldBe(
        [
            "user: start the dev server",
            "assistant: The dev server is running.",
            "user: what port is it on?",
            "assistant: It listens on 20240."
        ]);

        host.Starts.Count.ShouldBe(1, "no second process: the turn went to the one that runs the server");
        process.Terminated.ShouldBeFalse("the dev server keeps running");
        process.Prompts.Count.ShouldBe(2);
        process.Prompts[1].ShouldContain("what port is it on?");

        // Later the server stops: the CLI answers by itself, as a reply of its own after the user's turn
        process.Output.Emit([.. TaskFinished(), Init(), Text("msg_late", "The dev server has stopped."), Result("The dev server has stopped.", "task-notification")]);
        await secondExecution.WaitAsync(TestTimeout);

        secondRun = await ReloadRunAsync(second);
        secondRun.Status.ShouldBe(CliRunStatus.Completed, secondRun.Error);
        (await RepliesAsync(threadId)).ShouldBe(
        [
            "user: start the dev server",
            "assistant: The dev server is running.",
            "user: what port is it on?",
            "assistant: It listens on 20240.",
            "assistant: The dev server has stopped."
        ]);
        secondRun.AssistantMessageId.ShouldNotBeNull();
        Finished.Select(f => f.RunId).ShouldBe([first.Id, second]);
    }

    /// <summary>
    /// A consumer that binds the process to the run that launched it (a model gateway with a per-run token in the
    /// process environment, authorized and billed by that run id) follows the handover from framework information alone:
    /// before the continued prompt reaches the process, the receiving run is linked to the launching one and the
    /// handover event has been raised, so the process's first model call is allowed and billed to the receiving run;
    /// along a chain of handovers it is always the last receiver; once that run ends, the process's calls are refused.
    /// </summary>
    [Fact]
    public async Task AGatewayThatAuthorizesByTheLaunchingRun_FollowsTheProcessFromRunToRun()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var ownership = new CliProcessOwnership(ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>());

        // The gateway: each prompt written to the process stands for the model call it makes next, authorized by the
        // run id the process was launched with
        var calls = new List<(string Prompt, CliProcessOwner? Owner, int HandoversSeen)>();
        var process = new DevServerProcess(prompt => prompt.Contains("what port") ? "It listens on 20240." : prompt.Contains("restart") ? "Restarted." : null);
        process.OnPrompt = prompt => calls.Add((prompt, ownership.GetCurrentOwnerAsync(first.Id).GetAwaiter().GetResult(), HandedOver.Count));
        var host = TestCliHost.Starting(() => process);

        var firstExecution = Executor(host).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        (await ownership.GetCurrentOwnerAsync(first.Id)).ShouldNotBeNull().RunId.ShouldBe(first.Id, "a process belongs to the run that started it");

        var second = await EnqueueAsync(threadId, "what port is it on?");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);
        var secondExecution = Executor(host).ExecuteAsync(second, CancellationToken.None);
        await firstExecution.WaitAsync(TestTimeout);
        await WaitingAsync(second);

        // (1) allowed, (2) billed to the receiving run: at the moment its prompt reached the process
        var continued = calls.Single(c => c.Prompt.Contains("what port"));
        var owner = continued.Owner.ShouldNotBeNull("the receiving run is known before its prompt reaches the process");
        owner.RunId.ShouldBe(second);
        owner.LaunchedByRunId.ShouldBe(first.Id);
        owner.Status.ShouldBe(CliRunStatus.Running);
        owner.AgentId.ShouldBe(AgentId);
        owner.ThreadId.ShouldBe(threadId);
        continued.HandoversSeen.ShouldBe(1, "the handover event is raised before the prompt is written");
        var handover = HandedOver.ShouldHaveSingleItem();
        (handover.FromRunId, handover.ToRunId, handover.LaunchedByRunId, handover.ThreadId).ShouldBe((first.Id, second, first.Id, (Guid?)threadId));
        (await ReloadRunAsync(second)).ProcessLaunchedByRunId.ShouldBe(first.Id);
        (await ReloadRunAsync(first.Id)).Status.ShouldBe(CliRunStatus.Completed);

        // A second handover on the same process: still found by the run that launched it, now the latest receiver
        var third = await EnqueueAsync(threadId, "restart it");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(third);
        var thirdExecution = Executor(host).ExecuteAsync(third, CancellationToken.None);
        await secondExecution.WaitAsync(TestTimeout);
        await WaitingAsync(third);

        calls.Single(c => c.Prompt.Contains("restart")).Owner.ShouldNotBeNull().RunId.ShouldBe(third);
        HandedOver.Last().ShouldSatisfyAllConditions(
            e => e.FromRunId.ShouldBe(second),
            e => e.ToRunId.ShouldBe(third),
            e => e.LaunchedByRunId.ShouldBe(first.Id, "the process environment is still the first run's"));
        (await ReloadRunAsync(third)).ProcessLaunchedByRunId.ShouldBe(first.Id);
        (await ownership.GetCurrentOwnerAsync(second)).ShouldBeNull("the second run never launched a process");

        // The server stops, the run answers and ends with its process: nothing owns it any more, calls are refused
        process.Output.Emit([.. TaskFinished(), Init(), Text("msg_late", "The dev server has stopped."), Result("The dev server has stopped.", "task-notification")]);
        await thirdExecution.WaitAsync(TestTimeout);
        (await ReloadRunAsync(third)).Status.ShouldBe(CliRunStatus.Completed);
        (await ownership.GetCurrentOwnerAsync(first.Id)).ShouldBeNull();
    }

    /// <summary>
    /// Stop still ends everything: stopping the run that took the process over ends the process, and the server with it.
    /// </summary>
    [Fact]
    public async Task StoppingTheRunThatTookTheProcessOver_EndsTheProcess()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(_ => "Sure.");
        var host = TestCliHost.Starting(() => process);

        var firstExecution = Executor(host).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var second = await EnqueueAsync(threadId, "and now?");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);

        using var stop = new CancellationTokenSource();
        var secondExecution = Executor(host).ExecuteAsync(second, stop.Token);
        await firstExecution.WaitAsync(TestTimeout);
        await WaitingAsync(second);
        process.Terminated.ShouldBeFalse();

        await DbContext.Set<CliRun>().Where(r => r.Id == second).ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));
        await stop.CancelAsync();
        await secondExecution.WaitAsync(TestTimeout);

        (await ReloadRunAsync(second)).Status.ShouldBe(CliRunStatus.Cancelled);
        process.Terminated.ShouldBeTrue("Stop ends the process and its background tasks");
    }

    /// <summary>
    /// The wait limit still ends everything, also after a handover: the run that took the process over waits on the
    /// same server, and when its wait runs out the process is ended. Its answer is kept, and it completes.
    /// </summary>
    [Fact]
    public async Task TheWaitLimit_StillEndsTheProcess_AfterAHandover()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(_ => "Sure.");
        var host = TestCliHost.Starting(() => process);

        var firstExecution = Executor(host).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var second = await EnqueueAsync(threadId, "and now?");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);

        await Executor(host, wait: TimeSpan.FromMilliseconds(300)).ExecuteAsync(second, CancellationToken.None).WaitAsync(TestTimeout);
        await firstExecution.WaitAsync(TestTimeout);

        var secondRun = await ReloadRunAsync(second);
        secondRun.Status.ShouldBe(CliRunStatus.Completed, secondRun.Error);
        process.Terminated.ShouldBeTrue("the wait ran out: the process and the server are ended");
        (await RepliesAsync(threadId)).Last().ShouldBe("assistant: Sure.");
        _handoffs.WaitingRuns().ShouldBeEmpty();
    }

    /// <summary>With no next message, the wait limit ends the waiting run and its process as before.</summary>
    [Fact]
    public async Task WithNoNextMessage_TheWaitLimitEndsTheProcess()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(_ => null);

        await Executor(TestCliHost.Starting(() => process), wait: TimeSpan.FromMilliseconds(300))
            .ExecuteAsync(first.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadRunAsync(first.Id)).Status.ShouldBe(CliRunStatus.Completed);
        process.Terminated.ShouldBeTrue();
        _handoffs.WaitingRuns().ShouldBeEmpty("a run that ended is no longer offered");
    }

    /// <summary>
    /// The next message picked another model: the running process cannot switch, so it is ended (the run's stream says
    /// why) and a new process resumes the same session for that turn. The message is still answered at once.
    /// </summary>
    [Fact]
    public async Task ANextMessageThatNeedsAnotherLaunch_StartsANewProcessOnTheSameSession()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(_ => null);
        var next = new ScriptedExitProcess([Init(), Text("msg_9", "Answered by the new process."), Result("Answered by the new process.")]);
        var host = TestCliHost.Starting(() => process);

        var firstExecution = Executor(host).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var second = await EnqueueAsync(threadId, "use the bigger model", model: "bigger-model");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);

        host.StartWith = _ => next;
        await Executor(host).ExecuteAsync(second, CancellationToken.None).WaitAsync(TestTimeout);
        await firstExecution.WaitAsync(TestTimeout);

        process.Terminated.ShouldBeTrue();
        host.Starts.Count.ShouldBe(2);
        host.Starts[1].Arguments.ShouldContain("bigger-model");
        var resume = host.Starts[1].Arguments.ToList();
        resume[resume.IndexOf("--resume") + 1].ShouldBe(Session);

        var secondRun = await ReloadRunAsync(second);
        secondRun.Status.ShouldBe(CliRunStatus.Completed, secondRun.Error);
        (await RepliesAsync(threadId)).Last().ShouldBe("assistant: Answered by the new process.");
        secondRun.ProcessLaunchedByRunId.ShouldBeNull("the new process is the run's own, launched with its own environment");
        HandedOver.ShouldBeEmpty("the process was never continued");
        var stopped = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == second && m.Type == CliAgentEventType.Status).Select(m => m.Content).ToListAsync();
        stopped.ShouldContain(c => c != null && c.Contains("different settings"));
    }

    /// <summary>
    /// A consumer's launch environment with a model-gateway token signed for each run (fresh bytes every time), marked as
    /// run-scoped with the upstream credential it signs in with as its identity.
    /// </summary>
    private sealed class PerRunTokenComposer(Func<string?> identity) : ICliLaunchEnvironmentComposer
    {
        public const string Variable = "ANTHROPIC_AUTH_TOKEN";

        private readonly CliLaunchEnvironmentComposer _inner = new(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance);

        public List<string> Issued { get; } = [];

        public async Task<CliLaunchEnvironment> ComposeAsync(
            CliProviderDescriptor provider, string executablePath, CliWorkspace workspace, ICliHostTarget target,
            CliAgentOptions options, CliLaunchProfile profile, CancellationToken cancellationToken)
        {
            var composed = await _inner.ComposeAsync(provider, executablePath, workspace, target, options, profile, cancellationToken);
            var token = $"token-{Guid.NewGuid():N}";
            Issued.Add(token);
            return composed with
            {
                Environment = new Dictionary<string, string>(composed.Environment, StringComparer.OrdinalIgnoreCase) { [Variable] = token },
                RunScopedVariables = new Dictionary<string, string?>(composed.RunScopedVariables, StringComparer.OrdinalIgnoreCase)
                {
                    [Variable] = identity()
                }
            };
        }
    }

    /// <summary>
    /// The consumer signs a new gateway token for every run, so the receiving run's launch environment never equals the
    /// live process's byte for byte. Marked run-scoped with the same identity, the token is no launch difference: the
    /// next message is answered on the same process, the server keeps running, and the process keeps the launching
    /// run's token (the consumer follows it to its current owner).
    /// </summary>
    [Fact]
    public async Task ARunScopedTokenWithTheSameIdentity_DoesNotPreventTheHandover()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(prompt => prompt.Contains("what port") ? "It listens on 20240." : null);
        var host = TestCliHost.Starting(() => process);
        var tokens = new PerRunTokenComposer(() => "gateway:platform");

        var firstExecution = CreateExecutor(() => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), host,
            new CliAgentOptions { Enabled = true }, handoffs: _handoffs, launchEnvironment: tokens).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var second = await EnqueueAsync(threadId, "what port is it on?");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);

        var secondExecution = CreateExecutor(() => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), host,
            new CliAgentOptions { Enabled = true }, handoffs: _handoffs, launchEnvironment: tokens).ExecuteAsync(second, CancellationToken.None);
        await firstExecution.WaitAsync(TestTimeout);
        await WaitingAsync(second);

        tokens.Issued.Count.ShouldBe(2);
        tokens.Issued[0].ShouldNotBe(tokens.Issued[1], "each run is signed a token of its own");
        host.Starts.Count.ShouldBe(1, "the token alone is no reason for a second process");
        host.Starts[0].Environment[PerRunTokenComposer.Variable].ShouldBe(tokens.Issued[0], "the process keeps the launching run's token");
        process.Terminated.ShouldBeFalse("the dev server keeps running");
        (await RepliesAsync(threadId)).Last().ShouldBe("assistant: It listens on 20240.");
        (await ReloadRunAsync(second)).ProcessLaunchedByRunId.ShouldBe(first.Id);
        HandedOver.ShouldHaveSingleItem().LaunchedByRunId.ShouldBe(first.Id);

        process.Output.Emit([.. TaskFinished(), Init(), Text("msg_late", "Stopped."), Result("Stopped.", "task-notification")]);
        await secondExecution.WaitAsync(TestTimeout);
    }

    /// <summary>
    /// The receiving run's token signs in with another upstream credential (its identity differs): that is a real launch
    /// difference, the process is ended and a new one resumes the same session with the receiving run's own token.
    /// </summary>
    [Fact]
    public async Task ARunScopedTokenWithAnotherIdentity_StartsANewProcess()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(_ => null);
        var next = new ScriptedExitProcess([Init(), Text("msg_9", "Answered by the new process."), Result("Answered by the new process.")]);
        var host = TestCliHost.Starting(() => process);
        var composed = 0;
        var tokens = new PerRunTokenComposer(() => composed++ == 0 ? "gateway:platform" : "gateway:own:42");

        var firstExecution = CreateExecutor(() => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), host,
            new CliAgentOptions { Enabled = true }, handoffs: _handoffs, launchEnvironment: tokens).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var second = await EnqueueAsync(threadId, "use my own key now");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);

        host.StartWith = _ => next;
        await CreateExecutor(() => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), host,
            new CliAgentOptions { Enabled = true }, handoffs: _handoffs, launchEnvironment: tokens).ExecuteAsync(second, CancellationToken.None).WaitAsync(TestTimeout);
        await firstExecution.WaitAsync(TestTimeout);

        process.Terminated.ShouldBeTrue();
        host.Starts.Count.ShouldBe(2);
        host.Starts[1].Environment[PerRunTokenComposer.Variable].ShouldBe(tokens.Issued[^1], "the new process runs with the receiving run's own token");
        (await ReloadRunAsync(second)).Status.ShouldBe(CliRunStatus.Completed);
        (await RepliesAsync(threadId)).Last().ShouldBe("assistant: Answered by the new process.");
        HandedOver.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- a target that gives each run its environment

    private const string Endpoint = "https://platform.test/mcp";

    /// <summary>
    /// A consumer's execution target that learns which run it launches for from the write-back entry composed for that run
    /// (the platform's MCP channel with the run's token), and gives the launch profile the run's environment (its
    /// database, its git identity) only for a run it learned that way. In production each run has a target of its own;
    /// here one host serves every run, so a learned token counts for the next profile asked only.
    /// </summary>
    private sealed class RunEnvironmentTarget
    {
        public const string Database = "DATABASE_URL";
        public const string GitAuthor = "GIT_AUTHOR_NAME";

        private string? _learned;

        public RunEnvironmentTarget(Func<ICliProcess> process)
        {
            Host = new TestCliHost((_, _) => Task.FromResult(process()), writeBackEntry: channel =>
            {
                _learned = channel.Token;
                Learned.Add(channel.Token);
                return channel.ToHttpEntry();
            })
            {
                LaunchProfile = _ => Interlocked.Exchange(ref _learned, null) is null
                    ? CliLaunchProfile.Default
                    : CliLaunchProfile.Default with
                    {
                        Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [Database] = "postgres://session-db",
                            [GitAuthor] = "Platform Agent"
                        }
                    }
            };
        }

        public TestCliHost Host { get; }

        /// <summary>The write-back tokens the target was told of, in order.</summary>
        public List<string> Learned { get; } = [];
    }

    private static CliAgentOptions WriteBackOptions(TimeSpan? wait = null) => new()
    {
        Enabled = true,
        BackgroundTaskWait = wait ?? TimeSpan.FromMinutes(30),
        WriteBack = new CliWriteBackOptions { Enabled = true, McpEndpoint = Endpoint }
    };

    /// <summary>An executor that prepares real workspaces (on the host's file system) with real MCP configs.</summary>
    private CliRunExecutor PreparingExecutor(TestCliHost host, CliAgentOptions options) => CreateExecutor(
        () => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), host, options, handoffs: _handoffs,
        workspacePreparer: new FileSystemWorkspacePreparer(Options(options), NullLogger<FileSystemWorkspacePreparer>.Instance),
        mcpConfigComposer: new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance));

    /// <summary>The write-back token in the MCP config a process was launched with (read while its run is in progress).</summary>
    private static async Task<string> WriteBackTokenOfAsync(TestCliHost host, CliProcessSpec spec)
    {
        var arguments = spec.Arguments.ToList();
        var path = arguments[arguments.IndexOf("--mcp-config") + 1];
        var config = JsonNode.Parse(await host.FileSystem.ReadAllTextAsync(path, CancellationToken.None)).ShouldNotBeNull();
        var authorization = config["mcpServers"]?["tnzi"]?["headers"]?["Authorization"]?.GetValue<string>();
        return authorization.ShouldNotBeNull()["Bearer ".Length..];
    }

    /// <summary>The run the platform's write-back server takes a token for; null when it refuses it.</summary>
    private async Task<Guid?> RunOfTokenAsync(string token)
        => (await new CliRunTokenService(ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(), NullLogger<CliRunTokenService>.Instance)
            .ValidateAsync(token))?.RunId;

    /// <summary>
    /// A target that gives a run its environment only once it has heard of that run's write-back channel: the receiving
    /// run tells it of the channel of the process it takes over, so the profile it is answered matches the one the
    /// process was launched with, and the turn is answered on the same process with the dev server still running.
    /// The process's token counts as the receiving run's.
    /// </summary>
    [Fact]
    public async Task ATargetThatGivesTheEnvironmentByTheWriteBackChannel_StillLetsTheNextRunContinueOnTheProcess()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(prompt => prompt.Contains("what port") ? "It listens on 20240." : null);
        var target = new RunEnvironmentTarget(() => process);
        var options = WriteBackOptions();

        var firstExecution = PreparingExecutor(target.Host, options).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        target.Host.Starts[0].Environment.ShouldContainKey(RunEnvironmentTarget.Database);
        var token = await WriteBackTokenOfAsync(target.Host, target.Host.Starts[0]);

        var second = await EnqueueAsync(threadId, "what port is it on?");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);
        var secondExecution = PreparingExecutor(target.Host, options).ExecuteAsync(second, CancellationToken.None);
        await firstExecution.WaitAsync(TestTimeout);
        await WaitingAsync(second);

        target.Host.Starts.Count.ShouldBe(1, "the receiving run's launch is the process's: no second process");
        process.Terminated.ShouldBeFalse("the dev server keeps running");
        target.Learned.ShouldBe([token, token], "the receiving run told the target of the process's own channel");
        (await RunOfTokenAsync(token)).ShouldBe(second, "the process's write-back calls count as the receiving run's");
        (await RepliesAsync(threadId)).Last().ShouldBe("assistant: It listens on 20240.");

        process.Output.Emit([.. TaskFinished(), Init(), Text("msg_late", "Stopped."), Result("Stopped.", "task-notification")]);
        await secondExecution.WaitAsync(TestTimeout);
        (await RunOfTokenAsync(token)).ShouldBeNull("the token ends with the run that held it last");
    }

    /// <summary>
    /// The next message needs another launch (another model): the new process that takes the handed-over one's place is
    /// launched like any other. The receiving run gets a workspace prepared for itself, with a write-back token of its own
    /// in the MCP config that the platform accepts for that run, and the target gives it its environment (database,
    /// git identity) as at a normal launch. The ended process's token is no longer accepted.
    /// </summary>
    [Fact]
    public async Task ANextMessageThatNeedsAnotherLaunch_TheNewProcessIsLaunchedWithTheReceivingRunsOwnWorkspace()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(_ => null);
        var next = new DevServerProcess(_ => null);
        var target = new RunEnvironmentTarget(() => process);
        var options = WriteBackOptions();

        var firstExecution = PreparingExecutor(target.Host, options).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var firstToken = await WriteBackTokenOfAsync(target.Host, target.Host.Starts[0]);
        (await RunOfTokenAsync(firstToken)).ShouldBe(first.Id);

        var second = await EnqueueAsync(threadId, "use the bigger model", model: "bigger-model");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);
        target.Host.StartWith = _ => next;
        var secondExecution = PreparingExecutor(target.Host, options).ExecuteAsync(second, CancellationToken.None);
        await firstExecution.WaitAsync(TestTimeout);
        await WaitingAsync(second);

        process.Terminated.ShouldBeTrue();
        target.Host.Starts.Count.ShouldBe(2);
        await NewProcessIsTheReceivingRunsAsync(target, second, firstToken);
        (await ReloadRunAsync(second)).ProcessLaunchedByRunId.ShouldBeNull();

        next.Output.Emit([.. TaskFinished(), Init(), Text("msg_late", "Stopped."), Result("Stopped.", "task-notification")]);
        await secondExecution.WaitAsync(TestTimeout);
        (await ReloadRunAsync(second)).Status.ShouldBe(CliRunStatus.Completed);
    }

    /// <summary>
    /// The process's wait runs out just as the next run takes it over (after the handover was announced, before its
    /// prompt was written): the process ends its last turn and stops reading, and a new process resumes the session.
    /// That one too is launched with the receiving run's own workspace, token and environment, not the old run's.
    /// </summary>
    [Fact]
    public async Task AProcessWhoseWaitRunsOutDuringTheHandover_IsReplacedByOneWithTheReceivingRunsOwnWorkspace()
    {
        var wait = TimeSpan.FromSeconds(2);
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var process = new DevServerProcess(_ => null);
        var next = new DevServerProcess(_ => null);
        var target = new RunEnvironmentTarget(() => process);

        var firstExecution = PreparingExecutor(target.Host, WriteBackOptions(wait)).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);
        var waited = Stopwatch.StartNew();
        var firstToken = await WriteBackTokenOfAsync(target.Host, target.Host.Starts[0]);

        // The handover is announced before the prompt is written: hold it there until the process's wait has run out
        OnHandedOver = _ =>
        {
            var left = wait + TimeSpan.FromMilliseconds(500) - waited.Elapsed;
            if (left > TimeSpan.Zero)
            {
                Thread.Sleep(left);
            }
        };

        var second = await EnqueueAsync(threadId, "and now?");
        (await Processor().TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);
        target.Host.StartWith = _ => next;
        var secondExecution = PreparingExecutor(target.Host, WriteBackOptions()).ExecuteAsync(second, CancellationToken.None);
        await firstExecution.WaitAsync(TestTimeout);
        await WaitingAsync(second);

        HandedOver.ShouldHaveSingleItem("the process was taken over before its wait ran out");
        target.Host.Starts.Count.ShouldBe(2);
        process.Prompts.Count.ShouldBe(1, "the process stopped reading before the prompt was written");
        await NewProcessIsTheReceivingRunsAsync(target, second, firstToken);
        (await ReloadRunAsync(second)).ProcessLaunchedByRunId.ShouldBeNull("the new process is the run's own");

        next.Output.Emit([.. TaskFinished(), Init(), Text("msg_late", "Stopped."), Result("Stopped.", "task-notification")]);
        await secondExecution.WaitAsync(TestTimeout);
        (await ReloadRunAsync(second)).Status.ShouldBe(CliRunStatus.Completed);
    }

    /// <summary>
    /// The second process was launched for <paramref name="runId"/> as at a normal launch: its MCP config carries a token
    /// of the run's own (the target heard of it, the platform accepts it for the run), the target gave it the run's
    /// environment, and the token of the process it replaced is no longer accepted.
    /// </summary>
    private async Task NewProcessIsTheReceivingRunsAsync(RunEnvironmentTarget target, Guid runId, string replacedToken)
    {
        var relaunch = target.Host.Starts[1];
        relaunch.Environment.ShouldContainKey(RunEnvironmentTarget.Database);
        relaunch.Environment.ShouldContainKey(RunEnvironmentTarget.GitAuthor);
        var token = await WriteBackTokenOfAsync(target.Host, relaunch);
        token.ShouldNotBe(replacedToken);
        target.Learned.Last().ShouldBe(token, "the target heard of the receiving run's own channel before the launch");
        (await RunOfTokenAsync(token)).ShouldBe(runId, "the platform accepts the new process's token for the run it works for");
        (await RunOfTokenAsync(replacedToken)).ShouldBeNull("the ended process's token is no longer accepted");
    }

    /// <summary>
    /// Only the thread's first queued run is claimed onto the waiting process, and only when it belongs to the same
    /// agent and runtime; a run of another thread, or a second queued run, is left to the ordinary queue.
    /// </summary>
    [Fact]
    public async Task OnlyTheThreadsNextRun_IsClaimedOntoTheWaitingProcess()
    {
        var (threadId, firstMessage) = await SeedThreadWithQuestionAsync("start the dev server");
        var first = await SeedClaimedRunAsync(threadId, firstMessage, "start the dev server");
        var processor = Processor();

        (await processor.TryClaimContinuationAsync(CancellationToken.None)).ShouldBeNull("nothing waits yet");

        var process = new DevServerProcess(_ => "Sure.");
        var host = TestCliHost.Starting(() => process);
        var firstExecution = Executor(host).ExecuteAsync(first.Id, CancellationToken.None);
        await WaitingAsync(first.Id);

        var otherThread = await SeedThreadAsync();
        await EnqueueAsync(otherThread, "unrelated");
        (await processor.TryClaimContinuationAsync(CancellationToken.None)).ShouldBeNull("another thread's run waits for the ordinary queue");

        var second = await EnqueueAsync(threadId, "one");
        var third = await EnqueueAsync(threadId, "two");
        (await processor.TryClaimContinuationAsync(CancellationToken.None)).ShouldBe(second);
        (await processor.TryClaimContinuationAsync(CancellationToken.None)).ShouldBeNull("the thread already has a run taking the process over");

        await Executor(host, wait: TimeSpan.FromMilliseconds(300)).ExecuteAsync(second, CancellationToken.None).WaitAsync(TestTimeout);
        await firstExecution.WaitAsync(TestTimeout);
        (await ReloadRunAsync(third)).Status.ShouldBe(CliRunStatus.Queued);
    }
}
