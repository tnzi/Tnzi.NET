using Tnzi.AI.Events.Handlers;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// A thread's message queue end to end: messages written while a turn runs wait on the server and go out as one turn when
/// it ends, a stopped turn holds them, "send now" stops the turn and sends one message alone first, the sweep sends what a
/// lost end event left behind, and only the thread's owner reaches the queue.
/// </summary>
/// <remarks>
/// The real chat service, dispatch facade, CLI dispatcher and executor run on SQLite; only the CLI process is scripted.
/// The end of a run is delivered to the queue the way the event bus would: the executor's <see cref="CliRunFinishedEvent"/>
/// handed to <see cref="ChatQueueRunFinishedHandler"/>.
/// </remarks>
public class ChatQueueTests : CliChatTurnTestBase
{
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private const string ForbiddenModel = "forbidden-model";

    private readonly Mock<IChatAgentPolicy> _policy = new();

    /// <summary>An application's origin policy that would stamp any thread it is asked about.</summary>
    private readonly Mock<IThreadOriginPolicy> _originPolicy = new();
    private readonly AiChatOptions _chatOptions = new();

    /// <summary>An application's own merger, when a test sets one; otherwise the configured default.</summary>
    private IChatQueueMetadataMerger? _customMerger;

    /// <summary>An application's queue policy, when a test sets one; none = everything the owner asks is allowed.</summary>
    private IChatQueuePolicy? _queuePolicy;

    /// <summary>The application's handover policies (none by default: the framework's own rule decides).</summary>
    private List<IChatQueueHandoverPolicy> HandoverPolicies { get; } = [];

    public ChatQueueTests()
    {
        _policy.Setup(p => p.CheckAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _originPolicy.Setup(p => p.ResolveAsync(It.IsAny<ThreadOriginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<string?>("pass-device"));
    }

    private readonly string _databaseDirectory = TestScratch.CreateDirectory("chat-queue-db");

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // The executor's main loop, the queue's hand-over loop and the test thread read and write at once (most of all while
        // a reattached run replays): the base's one shared in-memory connection does not stand that (SQLite Error 5,
        // database is locked). A file database with a connection per scope queues the writers on SQLite's busy wait
        services.RemoveAll<DbContextOptions<CliChatDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<CliChatDbContext>>();
        services.AddDbContext<CliChatDbContext>(options => options
            .UseSqlite($"Data Source={Path.Combine(_databaseDirectory, "queue.sqlite")};Pooling=False")
            .EnableSensitiveDataLogging());

        services.AddScoped<IRepository<QueuedThreadMessage, Guid>, EFCoreRepository<CliChatDbContext, QueuedThreadMessage, Guid>>();

        var usagePolicy = new Mock<IUsagePolicyService> { CallBase = true };
        usagePolicy
            .Setup(p => p.ResolveChatModelAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid? _, Guid? _, string? _, string? model, CancellationToken _) => model == ForbiddenModel
                ? Result.Failure<string?>("The model 'forbidden-model' is not available to you.", 403, ErrorCodes.ModelNotAllowed)
                : Result.Success<string?>(model));

        services.AddScoped<IChatService>(sp => new ChatService(
            CreateFacade(new RecordingHandler()),
            new TestOptionsMonitor<AIOptions>(new AIOptions()),
            sp,
            agentPolicy: _policy.Object,
            threadService: sp.GetRequiredService<IAgentThreadService>(),
            threadInternal: sp.GetRequiredService<IAgentThreadInternalService>(),
            attachments: sp.GetRequiredService<IAgentAttachmentService>(),
            chatOptions: new TestOptionsMonitor<AiChatOptions>(_chatOptions),
            usagePolicy: usagePolicy.Object,
            originPolicy: _originPolicy.Object));
        services.AddScoped<IChatQueueMetadataMerger>(_ =>
            _customMerger ?? new ConfiguredChatQueueMetadataMerger(new TestOptionsMonitor<AiChatOptions>(_chatOptions)));
        services.AddScoped<IChatQueueDelivery>(sp => new ChatQueueDelivery(
            sp.GetRequiredService<IRepository<QueuedThreadMessage, Guid>>(),
            sp.GetRequiredService<IRepository<AgentAttachment, Guid>>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            CreateDispatcher(),
            new TestOptionsMonitor<AiChatOptions>(_chatOptions),
            NullLogger<ChatQueueDelivery>.Instance,
            sp.GetRequiredService<ICurrentTenant>(),
            HandoverPolicies));
        services.AddScoped<IChatQueueService>(sp => new ChatQueueService(
            sp,
            sp.GetRequiredService<IRepository<QueuedThreadMessage, Guid>>(),
            sp.GetRequiredService<IRepository<AgentThread, Guid>>(),
            sp.GetRequiredService<IRepository<AgentAttachment, Guid>>(),
            sp.GetRequiredService<IAgentThreadService>(),
            CreateDispatcher(),
            sp.GetRequiredService<IChatQueueDelivery>(),
            new TestOptionsMonitor<AiChatOptions>(_chatOptions),
            _queuePolicy,
            HandoverPolicies,
            [FilePolicy],
            sp.GetRequiredService<IAgentThreadInternalService>()));
    }

    public override void Dispose()
    {
        base.Dispose();
        TestScratch.Delete(_databaseDirectory);
    }

    private static Guid User => TestHelper.DefaultTestUserId;

    // ---------------------------------------------------------------- the turn ends: one turn for all

    [Fact]
    public async Task MessagesQueuedWhileATurnRuns_GoAsOneTurn_InTheOrderWritten_WhenItEnds()
    {
        var (threadId, firstRun) = await StartTurnAsync("refactor the parser");

        var a = await QueueAsync(threadId, "also update the docs");
        var b = await QueueAsync(threadId, "and run the tests", metadata: new() { ["device"] = JsonDocument.Parse("\"dev-7\"").RootElement });
        a.State.ShouldBe(QueuedMessageState.Queued);
        a.AfterRunId.ShouldBe(firstRun, "it waits for the turn that was running when it was written");
        (await RunsAsync(threadId)).Count.ShouldBe(1, "nothing goes while the turn runs");

        await CompleteAsync(firstRun);
        await DeliverEndOfLastRunAsync();

        var runs = await RunsAsync(threadId);
        runs.Count.ShouldBe(2);
        var next = runs[^1];
        next.Prompt.ShouldBe("also update the docs\n\nand run the tests");
        next.CreatorId.ShouldBe(User, "the turn is the user's, though it was started by a background handler");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty("a message that went out belongs to the thread");

        var question = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .Where(m => m.ThreadId == threadId && m.Role == MessageRole.User).OrderBy(m => m.Order).LastAsync();
        question.Content.ShouldBe("also update the docs\n\nand run the tests");
        question.MetadataJson.ShouldNotBeNull().ShouldContain("dev-7");
        b.Id.ShouldNotBe(a.Id);
    }

    [Fact]
    public async Task AMessageQueuedWhenNothingRuns_GoesAtOnce()
    {
        var threadId = await SeedThreadAsync();

        var queued = await QueueAsync(threadId, "hello");

        queued.State.ShouldBe(QueuedMessageState.Delivering, "it went out on the spot");
        var run = (await RunsAsync(threadId)).ShouldHaveSingleItem();
        run.Prompt.ShouldBe("hello");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    /// <summary>
    /// Queueing is the owner sending a message: the thread's last-message time (what the list orders by) moves when the
    /// message is queued, and its delivery after the turn writes the same time, so the row does not move again when the turn ends.
    /// </summary>
    [Fact]
    public async Task AQueuedMessage_MovesTheThreadWhenQueued_AndNotAgainWhenDelivered()
    {
        var (threadId, firstRun) = await StartTurnAsync("refactor the parser");
        var started = await LastUserMessageTimeAsync(threadId);

        var queued = await QueueAsync(threadId, "also update the docs");
        var row = await DbContext.Set<QueuedThreadMessage>().AsNoTracking().SingleAsync(m => m.Id == queued.Id);
        var afterQueueing = await LastUserMessageTimeAsync(threadId);
        afterQueueing.ShouldBe(row.CreationTime, TimeSpan.FromMilliseconds(1));
        afterQueueing.ShouldBeGreaterThanOrEqualTo(started);

        await CompleteAsync(firstRun);
        await DeliverEndOfLastRunAsync();

        (await RunsAsync(threadId)).Count.ShouldBe(2, "the queued message went out as the next turn");
        (await LastUserMessageTimeAsync(threadId)).ShouldBe(afterQueueing, TimeSpan.FromMilliseconds(1));
    }

    private async Task<DateTime> LastUserMessageTimeAsync(Guid threadId)
        => (await DbContext.Set<AgentThread>().AsNoTracking().IgnoreQueryFilters().SingleAsync(t => t.Id == threadId))
            .LastUserMessageTime.ShouldNotBeNull();

    // ---------------------------------------------------------------- the application's queue policy

    /// <summary>
    /// A session the application does not let queue (signed in on a temporary device with a one-time pass): queueing,
    /// editing and sending now are refused with 403 <c>AI_QUEUE_NOT_ALLOWED</c> and change nothing; listing and removing still work.
    /// The policy is told what is being done, in which thread, for whom.
    /// </summary>
    [Fact]
    public async Task ASessionThePolicyRefuses_CannotQueueEditOrSendNow()
    {
        var (threadId, firstRun) = await StartTurnAsync("work");
        var queued = await QueueAsync(threadId, "written before the pass session");
        var policy = new PassSessionPolicy(refuse: true);
        _queuePolicy = policy;

        var add = await Service.AddAsync(threadId, User, new QueueMessageRequestDto { Text = "from the pass device" });
        var edit = await Service.UpdateAsync(threadId, queued.Id, User, new UpdateQueuedMessageDto { Text = "changed" });
        var sendNow = await Service.SendNowAsync(threadId, queued.Id, User);

        foreach (var refused in new Result[] { add, edit, sendNow })
        {
            refused.Code.ShouldBe(403);
            refused.ErrorCode.ShouldBe(ErrorCodes.QueueNotAllowed);
        }

        var row = (await QueueRowsAsync(threadId)).ShouldHaveSingleItem();
        row.Text.ShouldBe("written before the pass session");
        row.SendNow.ShouldBeFalse();
        (await ReloadRunAsync(firstRun)).CancelRequested.ShouldBeFalse("a refused send-now stops nothing");

        policy.Seen.Select(r => r.Operation).ShouldBe([ChatQueueOperation.Queue, ChatQueueOperation.Edit, ChatQueueOperation.SendNow]);
        policy.Seen.ShouldAllBe(r => r.ThreadId == threadId && r.UserId == User && r.AgentId == AgentId);
        policy.Seen[0].MessageId.ShouldBeNull();
        policy.Seen[2].MessageId.ShouldBe(queued.Id);

        (await ListAsync(threadId)).ShouldHaveSingleItem("listing sends nothing, so the policy is not asked");
        (await Service.RemoveAsync(threadId, queued.Id, User)).Succeeded.ShouldBeTrue("removing sends nothing either");
        policy.Seen.Count.ShouldBe(3);
    }

    /// <summary>The policy's own status and code reach the caller; a policy that allows changes nothing.</summary>
    [Fact]
    public async Task ThePolicysOwnCodeIsKept_AndAnAllowingPolicyChangesNothing()
    {
        var (threadId, _) = await StartTurnAsync("work");
        _queuePolicy = new PassSessionPolicy(refuse: true, code: 409, errorCode: "APP_PASS_SESSION");

        var refused = await Service.AddAsync(threadId, User, new QueueMessageRequestDto { Text = "x" });
        refused.Code.ShouldBe(409);
        refused.ErrorCode.ShouldBe("APP_PASS_SESSION");

        _queuePolicy = new PassSessionPolicy(refuse: false);
        (await QueueAsync(threadId, "allowed")).State.ShouldBe(QueuedMessageState.Queued);
    }

    private sealed class PassSessionPolicy(bool refuse, int? code = null, string? errorCode = null) : IChatQueuePolicy
    {
        public List<ChatQueueRequest> Seen { get; } = [];

        public Task<Result> CanQueueAsync(ChatQueueRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add(request);
            return Task.FromResult(refuse
                ? Result.Failure("Not from a pass session.", code ?? 0, errorCode)
                : Result.Success());
        }
    }

    // ---------------------------------------------------------------- merging the turn's metadata

    /// <summary>
    /// The user queued a message to be planned first, then one with full permission: the turn they go out as must not run
    /// with full permission. A key listed in <c>AI:Chat:Queue:StrictestValues</c> takes the strictest value of the batch.
    /// </summary>
    [Fact]
    public async Task APermissionKeyListedAsStrictest_GivesTheMergedTurnTheStrictestValue()
    {
        _chatOptions.Queue.StrictestValues["permissionMode"] = ["plan", "ask", "full"];

        var stored = await MergedTurnMetadataAsync(("plan the migration", "plan"), ("then just do it", "full"));

        stored["permissionMode"].GetString().ShouldBe("plan");
    }

    /// <summary>The contrast that makes the hook necessary: without a ranking the value written last wins.</summary>
    [Fact]
    public async Task WithoutARanking_TheValueWrittenLastWins()
    {
        var stored = await MergedTurnMetadataAsync(("plan the migration", "plan"), ("then just do it", "full"));

        stored["permissionMode"].GetString().ShouldBe("full");
    }

    /// <summary>An application's own <see cref="IChatQueueMetadataMerger"/> decides, and is told which thread and user it is for.</summary>
    [Fact]
    public async Task AnApplicationsMerger_DecidesTheMergedTurnsMetadata()
    {
        var merger = new StrictestPermissionMerger();
        _customMerger = merger;

        var stored = await MergedTurnMetadataAsync(("plan the migration", "ask"), ("then just do it", "full"));

        stored["permissionMode"].GetString().ShouldBe("ask");
        merger.Seen.ShouldHaveSingleItem().Metadata.Count.ShouldBe(2);
        merger.Seen[0].UserId.ShouldBe(User);
    }

    /// <summary>Queues two messages behind a running turn, ends the turn, and reads the metadata the merged turn was stored with.</summary>
    private async Task<Dictionary<string, JsonElement>> MergedTurnMetadataAsync(params (string Text, string Mode)[] messages)
    {
        var (threadId, firstRun) = await StartTurnAsync("work");
        foreach (var (text, mode) in messages)
        {
            await QueueAsync(threadId, text, metadata: new() { ["permissionMode"] = JsonDocument.Parse($"\"{mode}\"").RootElement });
        }

        await CompleteAsync(firstRun);
        await DeliverEndOfLastRunAsync();

        (await QueueRowsAsync(threadId)).ShouldBeEmpty("the merged turn went out");
        var question = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .Where(m => m.ThreadId == threadId && m.Role == MessageRole.User).OrderBy(m => m.Order).LastAsync();
        question.Content.ShouldBe(string.Join("\n\n", messages.Select(m => m.Text)));
        return ChatTurnMetadata.Parse(question.MetadataJson).ShouldNotBeNull();
    }

    /// <summary>What an application writes when its ranking needs code: here, a fixed order for one key.</summary>
    private sealed class StrictestPermissionMerger : IChatQueueMetadataMerger
    {
        private static readonly string[] Order = ["plan", "ask", "full"];

        public List<ChatQueueMergeContext> Seen { get; } = [];

        public Task<IReadOnlyDictionary<string, JsonElement>?> MergeAsync(ChatQueueMergeContext context, CancellationToken cancellationToken = default)
        {
            Seen.Add(context);
            var strictest = context.Metadata
                .Select(m => m.TryGetValue("permissionMode", out var v) ? v : (JsonElement?)null)
                .Where(v => v is { ValueKind: JsonValueKind.String })
                .MinBy(v => Array.IndexOf(Order, v!.Value.GetString()));
            IReadOnlyDictionary<string, JsonElement>? merged = strictest is { } value
                ? new Dictionary<string, JsonElement> { ["permissionMode"] = value }
                : null;
            return Task.FromResult(merged);
        }
    }

    // ---------------------------------------------------------------- thread origins

    /// <summary>
    /// A queued turn continues the thread it was written in: it goes with that thread's id, so the chat service never asks
    /// the origin policy (that is for a message that starts a thread), no thread is created, and the thread keeps the origin
    /// it was started with. Both ways out: when the running turn ends, and at once when nothing runs.
    /// </summary>
    [Fact]
    public async Task AQueuedTurn_ContinuesItsThread_AndNeverReachesTheOriginPolicy()
    {
        var (threadId, firstRun) = await StartTurnAsync("triage the inbox");
        await DbContext.Set<AgentThread>().Where(t => t.Id == threadId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Origin, "email-rule"));
        var threadsBefore = await DbContext.Set<AgentThread>().CountAsync();

        await QueueAsync(threadId, "and archive the newsletters");
        await CompleteAsync(firstRun);
        await DeliverEndOfLastRunAsync();
        await CompleteAsync((await RunsAsync(threadId))[^1].Id);

        var idle = await QueueAsync(threadId, "then mark the rest read");
        idle.State.ShouldBe(QueuedMessageState.Delivering, "nothing ran, so it went at once");

        var runs = await RunsAsync(threadId);
        runs.Select(r => r.Prompt).ShouldBe(["triage the inbox", "and archive the newsletters", "then mark the rest read"]);
        runs.ShouldAllBe(r => r.ThreadId == threadId);
        (await DbContext.Set<AgentThread>().CountAsync()).ShouldBe(threadsBefore, "a queued turn starts no thread");
        (await DbContext.Set<AgentThread>().AsNoTracking().SingleAsync(t => t.Id == threadId)).Origin.ShouldBe("email-rule");
        _originPolicy.Verify(p => p.ResolveAsync(It.IsAny<ThreadOriginRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The control: a message that starts a thread does reach the policy, so the test above would see a call.</summary>
    [Fact]
    public async Task AMessageThatStartsAThread_ReachesTheOriginPolicy()
    {
        await SeedBindingAsync();
        using var scope = ServiceProvider.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        await foreach (var evt in chat.ChatStreamingAsync(new ChatRequestDto { AgentId = AgentId, UserId = User, Message = "new one" }))
        {
            if (evt.EventType == RunStreamEventTypes.RunStarted)
            {
                break;
            }
        }

        _originPolicy.Verify(p => p.ResolveAsync(It.Is<ThreadOriginRequest>(r => r.StartsThread), It.IsAny<CancellationToken>()), Times.Once);
        (await DbContext.Set<AgentThread>().AsNoTracking().SingleAsync()).Origin.ShouldBe("pass-device");
    }

    // ---------------------------------------------------------------- a stopped turn holds them

    [Fact]
    public async Task WhenTheTurnIsStopped_ItsMessagesAreHeld_AndGoOnlyWhenSentNow()
    {
        var (threadId, firstRun) = await StartTurnAsync("delete the old logs");
        var queued = await QueueAsync(threadId, "then compress the rest");
        var handed = await InsertRowAsync(threadId, "and keep a copy", QueuedMessageState.HandedOver, TimeSpan.Zero);
        await DbContext.Set<QueuedThreadMessage>().Where(m => m.Id == handed)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.HandedToRunId, (Guid?)firstRun).SetProperty(m => m.AfterRunId, (Guid?)firstRun));

        (await CreateDispatcher().CancelAsync(firstRun)).Succeeded.ShouldBeTrue();
        await DeliverEndAsync(firstRun);

        var list = await ListAsync(threadId);
        list.Count.ShouldBe(2);
        foreach (var held in list)
        {
            held.State.ShouldBe(QueuedMessageState.Held, "stopping means 'do not go on like this'");
            // The reason is on the row, so a page that lists it now says the same as one opened later
            held.FailureCode.ShouldBe(ErrorCodes.QueuedTurnStopped);
            held.Failure.ShouldBe("The turn was stopped, so this was not sent.");
        }

        (await RunsAsync(threadId)).Count.ShouldBe(1, "a held message does not go by itself");
        await Service.RemoveAsync(threadId, handed, User);

        var sent = await Service.SendNowAsync(threadId, queued.Id, User);

        sent.Succeeded.ShouldBeTrue(sent.Message);
        sent.Data!.State.ShouldBe(QueuedMessageState.Delivering);
        (await RunsAsync(threadId))[^1].Prompt.ShouldBe("then compress the rest");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- send now

    /// <summary>
    /// The fallback for a turn that cannot take a message while it works (an ACP agent): send now
    /// stops it and sends that message alone first, and the list says beforehand that it will stop the turn.
    /// </summary>
    [Fact]
    public async Task SendNow_ToATurnThatCannotTakeIt_StopsTheTurn_SendsThatMessageAloneFirst_AndTheRestWaitForTheTurnItStarted()
    {
        var (threadId, firstRun) = await StartTurnAsync("write the release notes");
        await CannotTakeMessagesAsync(firstRun);
        var a = await QueueAsync(threadId, "mention the new flag");
        var b = await QueueAsync(threadId, "stop, use the template instead");

        (await ListAsync(threadId)).ShouldAllBe(m => m.SendNowStopsTurn, "the list says so before the user presses it");
        var sent = await Service.SendNowAsync(threadId, b.Id, User);

        sent.Succeeded.ShouldBeTrue(sent.Message);
        (await ReloadRunAsync(firstRun)).Status.ShouldBe(CliRunStatus.Cancelled, "a turn that cannot take the message is stopped for it");
        var runs = await RunsAsync(threadId);
        runs.Count.ShouldBe(2);
        var urgent = runs[^1];
        urgent.Prompt.ShouldBe("stop, use the template instead", "it goes alone");

        var rest = (await QueueRowsAsync(threadId)).ShouldHaveSingleItem();
        rest.Id.ShouldBe(a.Id);
        rest.State.ShouldBe(QueuedMessageState.Queued, "not held: the user stopped the turn for a message, not to stop");
        rest.AfterRunId.ShouldBe(urgent.Id, "it waits for the turn the urgent message started");

        // The end event of the stopped turn comes later and finds nothing left to do.
        await DeliverEndAsync(firstRun);
        (await RunsAsync(threadId)).Count.ShouldBe(2);

        await CompleteAsync(urgent.Id);
        await DeliverEndOfLastRunAsync();

        var after = await RunsAsync(threadId);
        after.Count.ShouldBe(3);
        after[^1].Prompt.ShouldBe("mention the new flag");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SendNow_WhileTheTurnIsBeingStopped_GoesWhenItsEndArrives()
    {
        var (threadId, firstRun) = await StartTurnAsync("scan the repository");
        await ClaimAsync(firstRun); // claimed by a host: stopping it takes the executor's turn, not an instant write
        await CannotTakeMessagesAsync(firstRun);
        var queued = await QueueAsync(threadId, "only the src folder");

        var marked = await Service.SendNowAsync(threadId, queued.Id, User);

        marked.Data!.State.ShouldBe(QueuedMessageState.Queued);
        marked.Data.SendNow.ShouldBeTrue();
        (await ReloadRunAsync(firstRun)).CancelRequested.ShouldBeTrue();
        (await RunsAsync(threadId)).Count.ShouldBe(1, "it waits until the stopped turn is over");

        await MarkCancelledAsync(firstRun);
        await DeliverEndAsync(firstRun);

        (await RunsAsync(threadId))[^1].Prompt.ShouldBe("only the src folder");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- the sweep makes up for a lost event

    [Fact]
    public async Task AfterARestart_TheSweepSendsWhatALostEndEventLeftBehind()
    {
        var (threadId, firstRun) = await StartTurnAsync("bump the versions");
        await QueueAsync(threadId, "and tag the release");

        await CompleteAsync(firstRun); // the service stops before the end event reaches the queue

        (await Delivery.SweepAsync()).ShouldBe(1);

        (await RunsAsync(threadId))[^1].Prompt.ShouldBe("and tag the release");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSweep_LeavesMessagesOfARunningTurnAlone()
    {
        var (threadId, _) = await StartTurnAsync("long job");
        await QueueAsync(threadId, "next");

        (await Delivery.SweepAsync()).ShouldBe(0);

        (await QueueRowsAsync(threadId)).ShouldHaveSingleItem().State.ShouldBe(QueuedMessageState.Queued);
    }

    [Fact]
    public async Task TheSweep_SendsAStrandedMessage_AndHoldsOneInterruptedWhileGoing()
    {
        var threadId = await SeedThreadAsync();
        var stranded = await InsertRowAsync(threadId, "was to go at once", QueuedMessageState.Queued, age: TimeSpan.FromMinutes(5));
        var interrupted = await InsertRowAsync(threadId, "was going", QueuedMessageState.Delivering, age: TimeSpan.FromMinutes(10));
        var otherThread = await SeedThreadAsync();
        var fresh = await InsertRowAsync(otherThread, "is going at once right now", QueuedMessageState.Queued, age: TimeSpan.Zero);

        await Delivery.SweepAsync();

        (await RunsAsync(threadId)).ShouldHaveSingleItem().Prompt.ShouldBe("was to go at once");
        (await RunsAsync(otherThread)).ShouldBeEmpty();
        (await QueueRowsAsync(otherThread)).Single(r => r.Id == fresh).State
            .ShouldBe(QueuedMessageState.Queued, "a message that may be going at once is left to its own sending");
        var rows = await QueueRowsAsync(threadId);
        rows.ShouldNotContain(r => r.Id == stranded);
        var held = rows.Single(r => r.Id == interrupted);
        held.State.ShouldBe(QueuedMessageState.Held, "whether it reached the thread is unknown: the user decides");
        held.FailureCode.ShouldBe(ErrorCodes.QueuedMessageInterrupted);
    }

    // ---------------------------------------------------------------- what the user may do

    [Fact]
    public async Task OnlyTheThreadsOwner_ReachesItsQueue()
    {
        var (threadId, _) = await StartTurnAsync("private work");
        var queued = await QueueAsync(threadId, "mine");

        (await Service.GetListAsync(threadId, SomeoneElse)).Code.ShouldBe(404);
        (await Service.AddAsync(threadId, SomeoneElse, new QueueMessageRequestDto { Text = "theirs" })).Code.ShouldBe(404);
        (await Service.UpdateAsync(threadId, queued.Id, SomeoneElse, new UpdateQueuedMessageDto { Text = "changed" })).Code.ShouldBe(404);
        (await Service.RemoveAsync(threadId, queued.Id, SomeoneElse)).Code.ShouldBe(404);
        (await Service.SendNowAsync(threadId, queued.Id, SomeoneElse)).ErrorCode.ShouldBe(ErrorCodes.ThreadNotFound);

        var row = (await QueueRowsAsync(threadId)).ShouldHaveSingleItem();
        row.Text.ShouldBe("mine");
        row.SendNow.ShouldBeFalse();
        (await ListAsync(threadId)).ShouldHaveSingleItem("the owner still sees it");
    }

    [Fact]
    public async Task TheOwner_EditsAndRemoves_ButNotWhileItGoes()
    {
        var (threadId, _) = await StartTurnAsync("work");
        var a = await QueueAsync(threadId, "first words");
        var b = await QueueAsync(threadId, "second");

        var edited = await Service.UpdateAsync(threadId, a.Id, User, new UpdateQueuedMessageDto { Text = "  better words  " });
        edited.Data!.Text.ShouldBe("better words");
        (await Service.UpdateAsync(threadId, a.Id, User, new UpdateQueuedMessageDto { Text = "  " })).ErrorCode.ShouldBe(ErrorCodes.InvalidContent);

        (await Service.RemoveAsync(threadId, b.Id, User)).Succeeded.ShouldBeTrue();
        (await Service.RemoveAsync(threadId, b.Id, User)).ErrorCode.ShouldBe(ErrorCodes.QueuedMessageNotFound);

        await DbContext.Set<QueuedThreadMessage>().Where(m => m.Id == a.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.State, QueuedMessageState.Delivering));
        (await Service.UpdateAsync(threadId, a.Id, User, new UpdateQueuedMessageDto { Text = "late" })).ErrorCode.ShouldBe(ErrorCodes.QueuedMessageDelivering);
        (await Service.RemoveAsync(threadId, a.Id, User)).Code.ShouldBe(409);
        (await Service.SendNowAsync(threadId, a.Id, User)).Code.ShouldBe(409);
    }

    [Fact]
    public async Task AFullQueue_RefusesTheNextMessage()
    {
        _chatOptions.Queue.MaxMessagesPerThread = 2;
        var (threadId, _) = await StartTurnAsync("work");
        await QueueAsync(threadId, "one");
        await QueueAsync(threadId, "two");

        var third = await Service.AddAsync(threadId, User, new QueueMessageRequestDto { Text = "three" });

        third.Code.ShouldBe(409);
        third.ErrorCode.ShouldBe(ErrorCodes.QueueFull);
    }

    // ---------------------------------------------------------------- the user's rules apply when it goes

    [Fact]
    public async Task AModelTheUserMayNotUse_HoldsTheMessage_WithTheReason()
    {
        var (threadId, firstRun) = await StartTurnAsync("draft the email");
        await QueueAsync(threadId, "use the expensive one", model: ForbiddenModel);

        await CompleteAsync(firstRun);
        await DeliverEndOfLastRunAsync();

        (await RunsAsync(threadId)).Count.ShouldBe(1, "a refused turn leaves no run");
        var held = (await ListAsync(threadId)).ShouldHaveSingleItem();
        held.State.ShouldBe(QueuedMessageState.Held);
        held.FailureCode.ShouldBe(ErrorCodes.ModelNotAllowed);
        held.Failure.ShouldNotBeNull().ShouldContain("forbidden-model");
    }

    [Fact]
    public async Task AnAgentTheUserMayNoLongerChatWith_HoldsTheMessage()
    {
        var (threadId, firstRun) = await StartTurnAsync("hello");
        await QueueAsync(threadId, "are you there?");
        _policy.Setup(p => p.CheckAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("This agent is not available for chat.", 403, ErrorCodes.ChatAgentNotAllowed));

        await CompleteAsync(firstRun);
        await DeliverEndOfLastRunAsync();

        var held = (await ListAsync(threadId)).ShouldHaveSingleItem();
        held.State.ShouldBe(QueuedMessageState.Held);
        held.FailureCode.ShouldBe(ErrorCodes.ChatAgentNotAllowed);
    }

    [Fact]
    public async Task InvalidMetadata_IsRefusedWhenQueued()
    {
        var (threadId, _) = await StartTurnAsync("work");

        var refused = await Service.AddAsync(threadId, User, new QueueMessageRequestDto
        {
            Text = "x", Metadata = new() { ["1bad key"] = JsonDocument.Parse("1").RootElement }
        });

        refused.ErrorCode.ShouldBe(ErrorCodes.InvalidTurnMetadata);
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- the thread goes, its queue goes

    [Fact]
    public async Task DeletingTheThread_RemovesItsQueuedMessages()
    {
        var (threadId, _) = await StartTurnAsync("work");
        await QueueAsync(threadId, "later");
        var otherThread = await SeedThreadAsync();
        await InsertRowAsync(otherThread, "another thread's", QueuedMessageState.Held, TimeSpan.Zero);

        var cleanup = new ThreadCleanupHandler(
            NullLogger<ThreadCleanupHandler>.Instance,
            queueRepository: ServiceProvider.GetRequiredService<IRepository<QueuedThreadMessage, Guid>>());
        await cleanup.HandleAsync(new ThreadDeletedEvent { ThreadId = threadId });

        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
        (await QueueRowsAsync(otherThread)).ShouldHaveSingleItem();
    }

    // ---------------------------------------------------------------- handed to the running turn

    /// <summary>
    /// The case from real use: a long turn (a build and its tests) and a message written while it works. The message goes
    /// into the running claude process (its id as the uuid, priority "next"), the agent takes it in between two steps and
    /// carries on: nothing is stopped, no second turn starts, and the thread reads "the answer so far, the user's message, the
    /// answer to it", as it will after a reload. While it is in the agent's hands it cannot be edited or removed.
    /// </summary>
    [Fact]
    public async Task AQueuedMessage_IsHandedToTheRunningProcess_AndTakenInWithoutStoppingTheTurn()
    {
        var (threadId, runId) = await StartTurnAsync("build and test");
        var claude = new WorkingClaude();
        var execution = await ExecuteAsync(runId, claude);
        await WaitUntilAsync(() => Task.FromResult(claude.Working), "the turn is at work");

        var queued = await QueueAsync(threadId, "also run the linter");
        await WaitUntilAsync(async () => (await QueueRowsAsync(threadId)).Single().State == QueuedMessageState.HandedOver, "it is handed over");

        (await Service.UpdateAsync(threadId, queued.Id, User, new UpdateQueuedMessageDto { Text = "never mind" })).Code.ShouldBe(409);
        (await Service.RemoveAsync(threadId, queued.Id, User)).Code.ShouldBe(409);
        claude.Handed.ShouldBe([queued.Id.ToString()]);
        using (var frame = JsonDocument.Parse(claude.Written.Single(l => l.Contains(queued.Id.ToString()))))
        {
            frame.RootElement.GetProperty("priority").GetString().ShouldBe("next");
        }

        claude.FinishTakingHanded("Linted too: no warnings.");
        await execution.WaitAsync(TestTimeout);

        var run = await ReloadRunAsync(runId);
        run.Status.ShouldBe(CliRunStatus.Completed, run.Error);
        run.CancelRequested.ShouldBeFalse("handing a message over stops nothing");
        run.TakesMessagesMidTurn.ShouldBe(true);
        (await RunsAsync(threadId)).ShouldHaveSingleItem("the message went into the turn that was running");
        (await ThreadAsync(threadId)).ShouldBe(
        [
            "user: build and test",
            "assistant: Building it now.",
            "user: also run the linter",
            "assistant: Linted too: no warnings."
        ]);

        var delivered = (await QueueRowsAsync(threadId)).ShouldHaveSingleItem();
        delivered.State.ShouldBe(QueuedMessageState.Delivered, "shown as taken in until the turn that took it ends");
        delivered.ThreadMessageId.ShouldNotBeNull();
        var taken = (await CreateDispatcher().GetMessagesAsync(runId)).Data!.Single(m => m.Type == CliAgentEventType.UserMessage);
        taken.UserMessage!.ThreadMessageId.ShouldBe(delivered.ThreadMessageId);

        await DeliverEndOfLastRunAsync();
        (await QueueRowsAsync(threadId)).ShouldBeEmpty("the turn that took it ended");
        (await RunsAsync(threadId)).ShouldHaveSingleItem();
    }

    /// <summary>
    /// Send now on a turn that takes messages: the held message (with an image) goes to the running agent at once, the turn
    /// is not cancelled, and the image goes with it and stays on its message in the thread.
    /// </summary>
    [Fact]
    public async Task SendNow_ToATurnThatTakesMessages_HandsItOverWithoutStoppingAnything()
    {
        var (threadId, runId) = await StartTurnAsync("refactor the module");
        var held = await InsertRowAsync(threadId, "use this layout", QueuedMessageState.Held, TimeSpan.Zero, await StoreImageAsync(threadId));
        var claude = new WorkingClaude();
        var execution = await ExecuteAsync(runId, claude);
        await WaitUntilAsync(() => Task.FromResult(claude.Working), "the turn is at work");
        (await ListAsync(threadId)).Single().SendNowStopsTurn.ShouldBeFalse("the list says it goes to the agent without stopping");

        var sent = await Service.SendNowAsync(threadId, held, User);

        sent.Succeeded.ShouldBeTrue(sent.Message);
        (await ReloadRunAsync(runId)).CancelRequested.ShouldBeFalse("send now does not stop a turn that can take the message");
        await WaitUntilAsync(() => Task.FromResult(claude.Handed.Count == 1), "it reaches the process");
        using (var frame = JsonDocument.Parse(claude.Written.Single(l => l.Contains(held.ToString()))))
        {
            frame.RootElement.GetProperty("message").GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("image");
        }

        claude.FinishTakingHanded("Moved to that layout.");
        await execution.WaitAsync(TestTimeout);

        (await ReloadRunAsync(runId)).Status.ShouldBe(CliRunStatus.Completed);
        var userMessage = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Content == "use this layout");
        var image = await DbContext.Set<AgentAttachment>().AsNoTracking().SingleAsync(a => a.MessageId == userMessage.Id);
        image.MediaType.ShouldBe("image/png");
    }

    /// <summary>
    /// A process that may be reattached (a remote host that can hand it back after a restart) takes messages mid-turn like
    /// any other: before the run starts the queue already says send now will not stop it, the message goes into the turn,
    /// and where it was written is recorded on the run until the process ends.
    /// </summary>
    [Fact]
    public async Task AProcessThatMayBeReattached_TakesMessagesMidTurn_AndRecordsWhereEachWasWritten()
    {
        var (threadId, runId) = await StartTurnAsync("long migration");
        await DbContext.Set<CliRuntime>().Where(r => r.Id == RuntimeId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Mode, CliRuntimeMode.RemoteDaemon));
        (await CreateDispatcher().TakesMessagesWhileRunningAsSystemAsync(runId)).ShouldBe(true, "a remote runtime is no reason to refuse");
        await DbContext.Set<CliRuntime>().Where(r => r.Id == RuntimeId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Mode, CliRuntimeMode.InProcess));

        var claude = new WorkingClaude(reattachHandle: "handle-1");
        var execution = await ExecuteAsync(runId, claude);
        await WaitUntilAsync(() => Task.FromResult(claude.Working), "the turn is at work");
        (await ReloadRunAsync(runId)).TakesMessagesMidTurn.ShouldBe(true);

        var queued = await QueueAsync(threadId, "and back up first");
        await WaitUntilAsync(() => Task.FromResult(claude.Handed.Count == 1), "it is written to the process");
        (await QueueRowsAsync(threadId)).Single().State.ShouldBe(QueuedMessageState.HandedOver);
        claude.Handed.ShouldBe([queued.Id.ToString()]);
        var recorded = CliQueuedInputRecord.Parse((await ReloadRunAsync(runId)).HandedInputs).ShouldHaveSingleItem();
        recorded.MessageId.ShouldBe(queued.Id);

        claude.FinishTakingHanded("Backed up, then migrated.");
        await execution.WaitAsync(TestTimeout);

        var run = await ReloadRunAsync(runId);
        run.Status.ShouldBe(CliRunStatus.Completed, run.Error);
        run.CancelRequested.ShouldBeFalse();
        run.HandedInputs.ShouldBeNull("the process ended: nothing is left to replay");
        (await ThreadAsync(threadId)).ShouldBe(
        [
            "user: long migration",
            "assistant: Building it now.",
            "user: and back up first",
            "assistant: Backed up, then migrated."
        ]);
        await DeliverEndOfLastRunAsync();
        (await RunsAsync(threadId)).ShouldHaveSingleItem("the message went into the turn that was running");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    /// <summary>
    /// The server stops after a message went into a reattachable process and before claude took it in. The process carries
    /// on alone and takes it in. After the restart the run is reattached and replayed from the first line: the replay hands
    /// the message to the adapter again where it was written (the process is not sent it a second time), so the stdin count,
    /// the event numbers and claude's report that it took the message in all line up. The message reaches the thread once,
    /// and a message queued after the restart reaches the process as a new line.
    /// </summary>
    [Fact]
    public async Task AfterARestart_AMessageHandedToAReattachableProcess_IsNeitherSentAgainNorLost_AndTheReplayStaysInStep()
    {
        var (threadId, runId) = await StartTurnAsync("long migration");
        var remote = new SimulatedRemoteProcess("proc-1", line => MidTurnClaudeFrames.UuidOf(line) is { } uuid
            ? [MidTurnClaudeFrames.Lifecycle(uuid, "queued")]
            : [MidTurnClaudeFrames.Init(), MidTurnClaudeFrames.Text("msg_1", "Building it now."), MidTurnClaudeFrames.ToolUse("msg_2", "call_1", "Bash")]);
        var host = TestCliHost.Starting(() => remote.Connect());

        // First execution: the turn is at work, a message is handed to it, then the server stops
        await ClaimAsync(runId);
        QueuedMessageDto before;
        using (var shutdown = new CancellationTokenSource())
        {
            var first = Task.Run(() => CreateQueueExecutor(host).ExecuteAsync(runId, shutdown.Token));
            await WaitUntilAsync(async () => (await RunEventsAsync(runId)).Any(m => m.Type == CliAgentEventType.ToolUse), "the tool runs");

            before = await QueueAsync(threadId, "and back up first");
            await WaitUntilAsync(() => Task.FromResult(remote.Stdin.Count == 2), "it reaches the process");
            await shutdown.CancelAsync();
            await first.WaitAsync(TestTimeout);

            var detached = await ReloadRunAsync(runId);
            detached.Status.ShouldBe(CliRunStatus.Running, "the process is left running on the host");
            detached.ReattachHandle.ShouldBe("proc-1");
            CliQueuedInputRecord.Parse(detached.HandedInputs).Select(e => e.MessageId).ShouldBe([before.Id]);
            (await QueueRowsAsync(threadId)).Single().State.ShouldBe(QueuedMessageState.HandedOver, "it is in the process, not back in the queue");
            remote.Terminated.ShouldBeFalse();

            // While the server is down claude takes it in and goes on with another tool
            remote.Emit(MidTurnClaudeFrames.ToolResult("call_1", "build ok"));
            remote.Emit(MidTurnClaudeFrames.Lifecycle(before.Id.ToString(), "started"));
            remote.Emit(MidTurnClaudeFrames.Text("msg_3", "Backing up first."));
            remote.Emit(MidTurnClaudeFrames.ToolUse("msg_4", "call_2", "Bash"));
        }

        var eventsBefore = await RunEventsAsync(runId);

        // Restart: the run is claimed for reattachment and replayed
        await DbContext.Set<CliRun>().Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.ClaimedByHostId, "TEST-HOST")
            .SetProperty(r => r.LeaseExpiresAt, DateTime.UtcNow.AddMinutes(5)));
        host.OnReattach = request =>
        {
            request.Handle.ShouldBe("proc-1");
            return CliReattachResult.Attached(remote.Connect());
        };
        var second = Task.Run(() => CreateQueueExecutor(host).ExecuteAsync(runId, CancellationToken.None));
        await WaitUntilAsync(
            async () => (await QueueRowsAsync(threadId)).Single().State == QueuedMessageState.Delivered, "the replay sees it taken in");

        // A message queued after the restart goes into the process as a new line
        var after = await QueueAsync(threadId, "and tag the release");
        await WaitUntilAsync(() => Task.FromResult(remote.Stdin.Count == 3), "the new message reaches the process");
        remote.Stdin.Count(l => l.Contains(before.Id.ToString(), StringComparison.Ordinal)).ShouldBe(1, "the process is never sent a message twice");
        MidTurnClaudeFrames.UuidOf(remote.Stdin[2]).ShouldBe(after.Id.ToString());

        remote.Emit(MidTurnClaudeFrames.ToolResult("call_2", "backup ok"));
        remote.Emit(MidTurnClaudeFrames.Lifecycle(after.Id.ToString(), "started"));
        remote.Emit(MidTurnClaudeFrames.Text("msg_5", "Migrated and tagged."));
        remote.Emit(MidTurnClaudeFrames.Result("Migrated and tagged.", "prompt", before.Id.ToString(), after.Id.ToString()));
        await second.WaitAsync(TestTimeout);

        var run = await ReloadRunAsync(runId);
        run.Status.ShouldBe(CliRunStatus.Completed, run.Error);
        run.HandedInputs.ShouldBeNull();
        run.ReattachHandle.ShouldBeNull();
        host.Starts.ShouldHaveSingleItem("one process, started once and reattached");

        var events = await RunEventsAsync(runId);
        events.Take(eventsBefore.Count).Select(m => m.Id).ShouldBe(eventsBefore.Select(m => m.Id));
        events.Select(m => m.Sequence).ShouldBe(Enumerable.Range(1, events.Count), "no event is written twice or skipped");
        events.Count(m => m.Type == CliAgentEventType.UserMessage).ShouldBe(2);

        (await ThreadAsync(threadId)).ShouldBe(
        [
            "user: long migration",
            "assistant: Building it now.",
            "user: and back up first",
            "assistant: Backing up first.",
            "user: and tag the release",
            "assistant: Migrated and tagged."
        ]);
        await DeliverEndOfLastRunAsync();
        (await RunsAsync(threadId)).ShouldHaveSingleItem("both messages went into the turn that was running");
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();
    }

    /// <summary>
    /// A message with a file (an .eml the agent opens with its tools) cannot go mid-turn: the file has to be placed before a
    /// process starts. It waits for the turn to end, the message after it waits with it (the order is kept), and the list
    /// says sending it now would stop the turn.
    /// </summary>
    [Fact]
    public async Task AMessageWithAFile_WaitsForTheTurnToEnd_AndTheOnesAfterItWaitToo()
    {
        FilePolicy.Accepts = true;
        var (threadId, runId) = await StartTurnAsync("sort the inbox");
        var claude = new WorkingClaude();
        var execution = await ExecuteAsync(runId, claude);
        await WaitUntilAsync(() => Task.FromResult(claude.Working), "the turn is at work");

        var withFile = await QueueAsync(threadId, "this one too", content: [Eml()]);
        var after = await QueueAsync(threadId, "then archive");
        await Task.Delay(300);

        claude.Handed.ShouldBeEmpty("the file cannot be placed mid-turn, and the next message does not jump ahead of it");
        var listed = await ListAsync(threadId);
        listed.Single(m => m.Id == withFile.Id).SendNowStopsTurn.ShouldBeTrue();
        listed.Single(m => m.Id == after.Id).State.ShouldBe(QueuedMessageState.Queued);

        claude.FinishTakingHanded("Sorted.");
        await execution.WaitAsync(TestTimeout);
        await DeliverEndOfLastRunAsync();

        var next = (await RunsAsync(threadId))[^1];
        next.Prompt.ShouldBe("this one too\n\nthen archive");
        var files = await DbContext.Set<AgentAttachment>().AsNoTracking().Where(a => a.RunId == next.Id).ToListAsync();
        files.ShouldHaveSingleItem().FileName.ShouldBe("invoice.eml");
    }

    /// <summary>
    /// Attachments under the same rules as a normal send: an image and an .eml queue with the message, are stored on the
    /// server (a reload lists them, their content is readable through the thread's attachment endpoint), and go with the
    /// turn the message becomes; the queued copies are then gone.
    /// </summary>
    [Fact]
    public async Task AttachmentsOfAQueuedMessage_SurviveAReload_AndGoWithItsTurn()
    {
        FilePolicy.Accepts = true;
        var (threadId, firstRun) = await StartTurnAsync("summarise the thread");
        await CannotTakeMessagesAsync(firstRun);

        var queued = await QueueAsync(threadId, "and these", content: [PngPart(), Eml()]);
        queued.Attachments.Select(a => a.FileName).ShouldBe(["shot.png", "invoice.eml"]);

        var reloaded = (await ListAsync(threadId)).ShouldHaveSingleItem();
        reloaded.Attachments.Select(a => (a.MediaType, a.FileName)).ShouldBe([("image/png", "shot.png"), ("message/rfc822", "invoice.eml")]);
        var content = await ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<IAgentAttachmentService>()
            .GetContentAsync(threadId, reloaded.Attachments[1].Id, User);
        Encoding.UTF8.GetString(content.Data!.Content).ShouldContain("Subject: Invoice");

        await CompleteAsync(firstRun);
        await DeliverEndOfLastRunAsync();

        var next = (await RunsAsync(threadId))[^1];
        next.Prompt.ShouldBe("and these");
        var sent = await DbContext.Set<AgentAttachment>().AsNoTracking().Where(a => a.RunId == next.Id).OrderBy(a => a.CreationTime).ToListAsync();
        sent.Select(a => a.FileName).ShouldBe(["shot.png", "invoice.eml"], ignoreOrder: true);
        sent.ShouldAllBe(a => a.MessageId == next.UserMessageId, "shown on the message the turn answers");
        (await DbContext.Set<AgentAttachment>().AsNoTracking().CountAsync(a => reloaded.Attachments.Select(x => x.Id).Contains(a.Id)))
            .ShouldBe(0, "the queued copies went with the message");
    }

    [Fact]
    public async Task AFileTheApplicationDoesNotTake_IsRefusedWhenQueued_AndRemovingAMessageRemovesItsAttachments()
    {
        var (threadId, _) = await StartTurnAsync("work");

        var refused = await Service.AddAsync(threadId, User, new QueueMessageRequestDto { Text = "see attached", Content = [Eml()] });
        refused.Code.ShouldBe(415);
        refused.ErrorCode.ShouldBe(ErrorCodes.UnsupportedMediaType);
        (await QueueRowsAsync(threadId)).ShouldBeEmpty();

        var withImage = await QueueAsync(threadId, "", content: [PngPart()]);
        withImage.Text.ShouldBe("", "an attachment alone is a message");
        (await Service.RemoveAsync(threadId, withImage.Id, User)).Succeeded.ShouldBeTrue();
        (await DbContext.Set<AgentAttachment>().AsNoTracking().CountAsync(a => a.ThreadId == threadId && a.MessageId == null)).ShouldBe(0);
    }

    /// <summary>
    /// A message carrying a key listed as a rule of the turn (<c>StrictestValues</c>) is not handed over by default: the
    /// running turn cannot apply it. An application's <see cref="IChatQueueHandoverPolicy"/> decides instead when it has one.
    /// </summary>
    [Fact]
    public async Task AMessageWithTheTurnsRules_WaitsForANewTurn_UnlessTheApplicationSaysOtherwise()
    {
        _chatOptions.Queue.StrictestValues["permissionMode"] = ["plan", "full"];
        var (threadId, runId) = await StartTurnAsync("work");
        var claude = new WorkingClaude();
        var execution = await ExecuteAsync(runId, claude);
        await WaitUntilAsync(() => Task.FromResult(claude.Working), "the turn is at work");
        var plan = new Dictionary<string, JsonElement> { ["permissionMode"] = JsonDocument.Parse("\"plan\"").RootElement };

        await QueueAsync(threadId, "plan the next step first", metadata: plan);
        await Task.Delay(300);
        claude.Handed.ShouldBeEmpty("a turn's rules cannot change mid-turn");

        var policy = new HandoverPolicy(allow: true);
        HandoverPolicies.Add(policy);
        await WaitUntilAsync(() => Task.FromResult(claude.Handed.Count == 1), "the application lets it go");
        policy.Seen.ShouldContain(r => !r.DefaultDecision && r.RunId == runId && r.Metadata!.ContainsKey("permissionMode"));

        claude.FinishTakingHanded("Planned.");
        await execution.WaitAsync(TestTimeout);
    }

    private sealed class HandoverPolicy(bool allow) : IChatQueueHandoverPolicy
    {
        public List<ChatQueueHandoverRequest> Seen { get; } = [];

        public Task<bool> CanHandOverAsync(ChatQueueHandoverRequest request, CancellationToken cancellationToken = default)
        {
            lock (Seen)
            {
                Seen.Add(request);
            }

            return Task.FromResult(allow);
        }
    }

    /// <summary>
    /// A claude process at work: the prompt starts an answer and a tool call that runs until the test finishes it; a user
    /// message written meanwhile is reported queued, and taken in after the tool result when the test finishes the turn
    /// (the order claude 2.1.322 reports it in, see <see cref="MidTurnClaudeFrames"/>).
    /// </summary>
    private sealed class WorkingClaude : ICliProcess
    {
        private readonly FakeCliAgentTransport _transport;
        private readonly ConcurrentQueue<string> _written = new();
        private readonly ConcurrentQueue<string> _handed = new();

        public WorkingClaude(string? reattachHandle = null)
        {
            ReattachHandle = reattachHandle;
            _transport = new FakeCliAgentTransport([], onWrite: line =>
            {
                _written.Enqueue(line);
                if (MidTurnClaudeFrames.UuidOf(line) is { } uuid)
                {
                    _handed.Enqueue(uuid);
                    return [MidTurnClaudeFrames.Lifecycle(uuid, "queued")];
                }

                Working = true;
                return [MidTurnClaudeFrames.Init(), MidTurnClaudeFrames.Text("msg_1", "Building it now."), MidTurnClaudeFrames.ToolUse("msg_2", "call_1", "Bash")];
            });
        }

        public bool Working { get; private set; }

        public IReadOnlyList<string> Written => [.. _written];

        public IReadOnlyList<string> Handed => [.. _handed];

        /// <summary>The tool finishes; claude takes in what was handed to it and answers, and the turn ends.</summary>
        public void FinishTakingHanded(string answer)
        {
            var handed = Handed;
            var lines = new List<string> { MidTurnClaudeFrames.ToolResult("call_1", "build ok") };
            lines.AddRange(handed.Select(u => MidTurnClaudeFrames.Lifecycle(u, "started")));
            lines.Add(MidTurnClaudeFrames.Text("msg_3", answer));
            lines.AddRange(handed.Select(u => MidTurnClaudeFrames.Lifecycle(u, "completed")));
            lines.Add(MidTurnClaudeFrames.Result(answer, ["prompt", .. handed]));
            _transport.Emit([.. lines]);
        }

        public ICliAgentTransport Transport => _transport;

        public int ProcessId => 4343;

        public string? ReattachHandle { get; }

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 0 : null;

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            HasExited = true;
            _transport.CompleteOutput();
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Claims the run and executes it on <paramref name="claude"/> with the real stream-json adapter, in the background.</summary>
    private async Task<Task> ExecuteAsync(Guid runId, WorkingClaude claude)
    {
        await ClaimAsync(runId);
        var executor = CreateExecutor(
            () => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance),
            TestCliHost.Starting(() => claude),
            new CliAgentOptions { Enabled = true, Approval = new CliApprovalOptions { PollInterval = TimeSpan.FromMilliseconds(50) } });
        return Task.Run(() => executor.ExecuteAsync(runId, CancellationToken.None));
    }

    /// <summary>The executor over the real stream-json adapter on <paramref name="host"/> (a host whose processes may be reattached).</summary>
    private CliRunExecutor CreateQueueExecutor(TestCliHost host)
        => CreateExecutor(
            () => new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance),
            host,
            new CliAgentOptions { Enabled = true, Approval = new CliApprovalOptions { PollInterval = TimeSpan.FromMilliseconds(50) } });

    private Task<List<CliRunMessage>> RunEventsAsync(Guid runId)
        => ReadAsync(db => db.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == runId).OrderBy(m => m.Sequence).ToListAsync());

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
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

    private async Task<List<string>> ThreadAsync(Guid threadId)
        => await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.Order)
            .Select(m => m.Role + ": " + m.Content)
            .ToListAsync();

    private static ImageContentPartDto PngPart() => new() { Base64Data = Convert.ToBase64String(Png), MediaType = "image/png", FileName = "shot.png" };

    private static DocumentContentPartDto Eml() => new()
    {
        Base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("From: billing@example.com\r\nSubject: Invoice\r\n\r\nPlease pay.")),
        MediaType = "message/rfc822",
        FileName = "invoice.eml"
    };

    /// <summary>An image stored as a queued message's attachment (in the thread, on no message yet).</summary>
    private async Task<Guid> StoreImageAsync(Guid threadId)
    {
        var ids = await ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<IAgentAttachmentService>().SaveAsync(
        [
            new NewAgentAttachment { Origin = AttachmentOrigin.Upload, MediaType = "image/png", Content = Png, FileName = "layout.png", ThreadId = threadId, OwnerId = User }
        ]);
        return ids[0];
    }

    // ---------------------------------------------------------------- helpers

    private IChatQueueService Service => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<IChatQueueService>();

    private IChatQueueDelivery Delivery => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<IChatQueueDelivery>();

    /// <summary>A thread with a turn queued on the CLI runtime (the user's first message), still running.</summary>
    private async Task<(Guid ThreadId, Guid RunId)> StartTurnAsync(string text)
    {
        var threadId = await SeedThreadAsync();
        using var scope = ServiceProvider.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        await foreach (var evt in chat.ChatStreamingAsync(new ChatRequestDto { AgentId = AgentId, ThreadId = threadId, UserId = User, Message = text }))
        {
            if (evt.EventType == RunStreamEventTypes.RunStarted)
            {
                return (threadId, evt.RunId!.Value);
            }
        }

        throw new InvalidOperationException("The turn did not start.");
    }

    private async Task<QueuedMessageDto> QueueAsync(
        Guid threadId, string text, string? model = null, Dictionary<string, JsonElement>? metadata = null, List<ContentPartDto>? content = null)
    {
        var result = await Service.AddAsync(threadId, User, new QueueMessageRequestDto { Text = text, Model = model, Metadata = metadata, Content = content });
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    private async Task<List<QueuedMessageDto>> ListAsync(Guid threadId)
    {
        var result = await Service.GetListAsync(threadId, User);
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    /// <summary>The queued run is claimed and runs to completion on the scripted CLI.</summary>
    private async Task CompleteAsync(Guid runId)
    {
        await ClaimAsync(runId);
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "done" }]);
        await CreateExecutor(adapter).ExecuteAsync(runId, CancellationToken.None).WaitAsync(TestTimeout);
        (await ReloadRunAsync(runId)).Status.ShouldBe(CliRunStatus.Completed);
    }

    /// <summary>The run's executor recorded that it cannot take messages while it runs (an ACP agent).</summary>
    private async Task CannotTakeMessagesAsync(Guid runId)
        => await DbContext.Set<CliRun>().Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.TakesMessagesMidTurn, (bool?)false));

    /// <summary>The host that runs it notices the stop and writes the end (as the executor would).</summary>
    private async Task MarkCancelledAsync(Guid runId)
        => await DbContext.Set<CliRun>().Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, CliRunStatus.Cancelled)
            .SetProperty(r => r.CompletedAt, DateTime.UtcNow));

    /// <summary>The bus hands the executor's end event to the queue's handler.</summary>
    private async Task DeliverEndOfLastRunAsync()
        => await new ChatQueueRunFinishedHandler(ServiceProvider.GetRequiredService<IServiceScopeFactory>()).HandleAsync(Finished[^1]);

    /// <summary>An end event for a run as it stands in the database.</summary>
    private async Task DeliverEndAsync(Guid runId)
    {
        var run = await ReloadRunAsync(runId);
        await new ChatQueueRunFinishedHandler(ServiceProvider.GetRequiredService<IServiceScopeFactory>())
            .HandleAsync(new CliRunFinishedEvent { RunId = run.Id, AgentId = run.AgentId, ThreadId = run.ThreadId, UserId = run.CreatorId, Status = run.Status });
    }

    private async Task<List<CliRun>> RunsAsync(Guid threadId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.ThreadId == threadId).OrderBy(r => r.CreationTime).ThenBy(r => r.Id).ToListAsync();

    /// <summary>
    /// Read through a context of its own: the executor's run repository is the test's <c>DbContext</c> (both resolved from the
    /// root provider), so polling it while a run executes would race the executor's own writes.
    /// </summary>
    private async Task<T> ReadAsync<T>(Func<CliChatDbContext, Task<T>> read)
    {
        using var scope = ServiceProvider.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<CliChatDbContext>());
    }

    private Task<List<QueuedThreadMessage>> QueueRowsAsync(Guid threadId)
        => ReadAsync(db => db.Set<QueuedThreadMessage>().AsNoTracking().Where(m => m.ThreadId == threadId).ToListAsync());

    private async Task<Guid> InsertRowAsync(Guid threadId, string text, QueuedMessageState state, TimeSpan age, Guid? attachmentId = null)
    {
        var row = new QueuedThreadMessage
        {
            Id = Guid.NewGuid(), ThreadId = threadId, UserId = User, AgentId = AgentId, Text = text, State = state,
            DeliveringSince = state == QueuedMessageState.Delivering ? DateTime.UtcNow - age : null,
            AttachmentIds = attachmentId is { } id ? JsonSerializer.Serialize(new[] { id }) : null
        };
        DbContext.Set<QueuedThreadMessage>().Add(row);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var queuedAt = DateTime.UtcNow - age;
        await DbContext.Set<QueuedThreadMessage>().Where(m => m.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.CreationTime, queuedAt));
        return row.Id;
    }
}
