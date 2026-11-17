using Tnzi.EventBus;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// Threads kept out of their owner's conversation lists: what server code makes unlisted is absent from the list (any status,
/// any sort, with or without a keyword) and from the attention counts, stays usable by id, and joins the list once its owner
/// writes in it (a chat turn or a queued message), not when server code runs a turn in it. <see cref="ThreadListing.Never"/>
/// stays out for good.
/// </summary>
/// <remarks>
/// The real chat service, queue, dispatch facade, CLI dispatcher and thread service run on SQLite; only the CLI process is scripted.
/// </remarks>
public class ThreadListingTests : CliChatTurnTestBase
{
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-99999999999a");

    private readonly string _databaseDirectory = TestScratch.CreateDirectory("thread-listing-db");
    private readonly List<ThreadListedEvent> _listed = [];

    private static Guid User => TestHelper.DefaultTestUserId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // A file database with a connection per scope: the queue's delivery and the test read and write at once
        services.RemoveAll<DbContextOptions<CliChatDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<CliChatDbContext>>();
        services.AddDbContext<CliChatDbContext>(options => options
            .UseSqlite($"Data Source={Path.Combine(_databaseDirectory, "listing.sqlite")};Pooling=False"));

        services.AddScoped<IRepository<QueuedThreadMessage, Guid>, EFCoreRepository<CliChatDbContext, QueuedThreadMessage, Guid>>();
        services.AddScoped<ICliAgentDispatcher>(_ => CreateDispatcher());

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(b => b.PublishAsync(It.IsAny<ThreadListedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThreadListedEvent, CancellationToken>((e, _) => _listed.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(eventBus.Object);

        var chatOptions = new AiChatOptions();
        services.AddScoped<IChatService>(sp => new ChatService(
            CreateFacade(new RecordingHandler()),
            new TestOptionsMonitor<AIOptions>(new AIOptions()),
            sp,
            threadService: sp.GetRequiredService<IAgentThreadService>(),
            threadInternal: sp.GetRequiredService<IAgentThreadInternalService>(),
            attachments: sp.GetRequiredService<IAgentAttachmentService>(),
            chatOptions: new TestOptionsMonitor<AiChatOptions>(chatOptions)));
        services.AddScoped<IChatQueueDelivery>(sp => new ChatQueueDelivery(
            sp.GetRequiredService<IRepository<QueuedThreadMessage, Guid>>(),
            sp.GetRequiredService<IRepository<AgentAttachment, Guid>>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            CreateDispatcher(),
            new TestOptionsMonitor<AiChatOptions>(chatOptions),
            NullLogger<ChatQueueDelivery>.Instance,
            sp.GetRequiredService<ICurrentTenant>(),
            []));
        services.AddScoped<IChatQueueService>(sp => new ChatQueueService(
            sp,
            sp.GetRequiredService<IRepository<QueuedThreadMessage, Guid>>(),
            sp.GetRequiredService<IRepository<AgentThread, Guid>>(),
            sp.GetRequiredService<IRepository<AgentAttachment, Guid>>(),
            sp.GetRequiredService<IAgentThreadService>(),
            CreateDispatcher(),
            sp.GetRequiredService<IChatQueueDelivery>(),
            new TestOptionsMonitor<AiChatOptions>(chatOptions),
            filePolicies: [FilePolicy]));
    }

    public override void Dispose()
    {
        base.Dispose();
        TestScratch.Delete(_databaseDirectory);
    }

    // ---------------------------------------------------------------- out of the lists, usable by id

    [Fact]
    public async Task AnUnlistedThread_IsAbsentFromTheList_WhateverTheStatusSortOrKeyword()
    {
        var listed = await SeedThreadAsync(ThreadListing.Listed, "Nightly backup check");
        var unlisted = await SeedThreadAsync(ThreadListing.UntilUserMessage, "Nightly report");
        var never = await SeedThreadAsync(ThreadListing.Never, "Nightly memory tidy");

        foreach (var status in Enum.GetValues<ThreadListStatus>())
        {
            foreach (var sort in Enum.GetValues<ThreadListSort>())
            {
                foreach (var keyword in new[] { null, "nightly" })
                {
                    var ids = (await ListAsync(new ThreadListQueryDto { CreatorId = User, Status = status, Sort = sort, Keyword = keyword }))
                        .Select(t => t.Id).ToList();
                    // None of them is archived: the archived view is empty, every other view has the listed thread only
                    ids.ShouldBe(status == ThreadListStatus.Archived ? [] : [listed], $"status {status}, sort {sort}, keyword {keyword ?? "(none)"}");
                }
            }
        }

        // The admin list (it sets IncludeUnlisted) sees them all, each with its listing
        var all = await ListAsync(new ThreadListQueryDto { IncludeUnlisted = true });
        all.Select(t => t.Id).ShouldBe([listed, unlisted, never], ignoreOrder: true);
        all.Single(t => t.Id == unlisted).Listing.ShouldBe(ThreadListing.UntilUserMessage);
        all.Single(t => t.Id == never).Listing.ShouldBe(ThreadListing.Never);
    }

    [Fact]
    public async Task AnUnlistedThread_IsNotCountedRunningOrUnread()
    {
        var listed = await SeedThreadAsync(ThreadListing.Listed);
        var unlisted = await SeedThreadAsync(ThreadListing.UntilUserMessage);
        foreach (var threadId in new[] { listed, unlisted })
        {
            await AddMessageAsync(threadId, MessageRole.User, "go");
            await MarkRepliedAsync(threadId);
            await SeedQueuedRunAsync(threadId);
        }

        var attention = await AttentionAsync();

        attention.RunningCount.ShouldBe(1);
        attention.RunningThreadIds.ShouldBe([listed]);
        attention.UnreadCount.ShouldBe(1);
        attention.UnreadThreadIds.ShouldBe([listed]);
    }

    [Fact]
    public async Task AnUnlistedThread_IsFullyUsableById()
    {
        var threadId = await SeedThreadAsync(ThreadListing.UntilUserMessage, "Nightly report");
        await AddMessageAsync(threadId, MessageRole.User, "write the report");
        await AddMessageAsync(threadId, MessageRole.Assistant, "Here it is.");
        await MarkRepliedAsync(threadId);

        using var scope = ServiceProvider.CreateScope();
        var threads = scope.ServiceProvider.GetRequiredService<IAgentThreadService>();

        var byId = (await threads.GetByIdAsync(threadId)).Data.ShouldNotBeNull();
        byId.Listing.ShouldBe(ThreadListing.UntilUserMessage);
        var detail = (await threads.GetDetailAsync(threadId)).Data.ShouldNotBeNull();
        detail.Listing.ShouldBe(ThreadListing.UntilUserMessage);
        detail.Unread.ShouldBeTrue();
        detail.Messages.Select(m => m.Content).ShouldBe(["write the report", "Here it is."]);
        (await threads.GetMessagesAsync(threadId, null, 50)).Data.ShouldNotBeNull().Messages.Count.ShouldBe(2);
        (await threads.IsOwnerAsync(threadId, User)).ShouldBeTrue();

        (await threads.MarkReadAsync(threadId)).Succeeded.ShouldBeTrue();
        (await threads.GetDetailAsync(threadId)).Data!.Unread.ShouldBeFalse();

        (await threads.DeleteAsync(threadId)).Succeeded.ShouldBeTrue();
        using var after = ServiceProvider.CreateScope();
        (await after.ServiceProvider.GetRequiredService<IAgentThreadService>().GetByIdAsync(threadId)).Code.ShouldBe(404);
    }

    // ---------------------------------------------------------------- the owner writes: listed

    [Fact]
    public async Task TheOwnersChatTurn_ListsTheThread_Once()
    {
        var threadId = await SeedThreadAsync(ThreadListing.UntilUserMessage);

        await ChatAsync(threadId, "thanks, and what about tomorrow?");

        (await ReloadAsync(threadId)).Listing.ShouldBe(ThreadListing.Listed);
        (await ListAsync(new ThreadListQueryDto { CreatorId = User })).Select(t => t.Id).ShouldBe([threadId]);
        _listed.ShouldHaveSingleItem().ThreadId.ShouldBe(threadId);
        _listed[0].UserId.ShouldBe(User);

        await ChatAsync(threadId, "and the day after?");
        _listed.Count.ShouldBe(1, "an already listed thread raises nothing");
    }

    [Fact]
    public async Task QueueingAMessage_ListsTheThread()
    {
        var threadId = await SeedThreadAsync(ThreadListing.UntilUserMessage);
        await SeedQueuedRunAsync(threadId);

        using (var scope = ServiceProvider.CreateScope())
        {
            var queued = await scope.ServiceProvider.GetRequiredService<IChatQueueService>()
                .AddAsync(threadId, User, new QueueMessageRequestDto { Text = "also check the logs" });
            queued.Succeeded.ShouldBeTrue(queued.Message);
        }

        (await ReloadAsync(threadId)).Listing.ShouldBe(ThreadListing.Listed);
        _listed.ShouldHaveSingleItem().ThreadId.ShouldBe(threadId);
    }

    [Fact]
    public async Task AServerDispatchedTurn_CreatesTheThreadUnlisted_AndDoesNotListIt()
    {
        await SeedBindingAsync();
        var facade = CreateFacade(new RecordingHandler());
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Report ready." }]);

        // The automation's own first message: in-process, trusted, on the user's behalf
        var first = new AgentRunRequest
        {
            AgentId = AgentId, UserId = User, UserMessage = "write the nightly report", TrustedToolSelection = true,
            ThreadOrigin = "Schedule", ThreadListing = ThreadListing.UntilUserMessage
        };
        await StreamThroughAsync(facade, adapter, first);
        var threadId = first.ThreadId.ShouldNotBeNull();

        var thread = await ReloadAsync(threadId);
        thread.Listing.ShouldBe(ThreadListing.UntilUserMessage);
        thread.CreatorId.ShouldBe(User);
        thread.Origin.ShouldBe("Schedule");

        // A later server-dispatched turn in the same thread does not list it either
        await StreamThroughAsync(facade, adapter, new AgentRunRequest
        {
            AgentId = AgentId, ThreadId = threadId, UserId = User, UserMessage = "and add the totals", TrustedToolSelection = true
        });

        (await ReloadAsync(threadId)).Listing.ShouldBe(ThreadListing.UntilUserMessage);
        (await ListAsync(new ThreadListQueryDto { CreatorId = User })).ShouldBeEmpty();
        _listed.ShouldBeEmpty();
    }

    [Fact]
    public async Task ANeverListedThread_StaysOut_WhenTheOwnerWritesInIt()
    {
        var threadId = await SeedThreadAsync(ThreadListing.Never);

        await ChatAsync(threadId, "what did you tidy?");

        (await ReloadAsync(threadId)).Listing.ShouldBe(ThreadListing.Never);
        (await ListAsync(new ThreadListQueryDto { CreatorId = User })).ShouldBeEmpty();
        _listed.ShouldBeEmpty();
    }

    [Fact]
    public async Task SomeoneElseWritingTheThreadsId_DoesNotListIt()
    {
        var threadId = await SeedThreadAsync(ThreadListing.UntilUserMessage);

        using var scope = ServiceProvider.CreateScope();
        var threads = scope.ServiceProvider.GetRequiredService<IAgentThreadInternalService>();
        (await threads.ListOnUserMessageAsync(threadId, SomeoneElse)).ShouldBeFalse();

        (await ReloadAsync(threadId)).Listing.ShouldBe(ThreadListing.UntilUserMessage);
        _listed.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- server code changes it

    [Fact]
    public async Task ServerCode_SetsTheListing_AndCreatesThreadsUnlisted()
    {
        Guid threadId;
        using (var scope = ServiceProvider.CreateScope())
        {
            await SeedBindingAsync();
            var created = await scope.ServiceProvider.GetRequiredService<IAgentThreadService>()
                .CreateAsync(new CreateAgentThreadDto { AgentId = AgentId, Title = "Memory tidy", Listing = ThreadListing.Never });
            created.Succeeded.ShouldBeTrue(created.Message);
            created.Data!.Listing.ShouldBe(ThreadListing.Never);
            threadId = created.Data.Id;
        }

        using (var scope = ServiceProvider.CreateScope())
        {
            var threads = scope.ServiceProvider.GetRequiredService<IAgentThreadInternalService>();
            (await threads.SetListingAsync(threadId, ThreadListing.Listed)).Succeeded.ShouldBeTrue();
            (await threads.SetListingAsync(Guid.NewGuid(), ThreadListing.Listed)).Code.ShouldBe(404);
        }

        (await ReloadAsync(threadId)).Listing.ShouldBe(ThreadListing.Listed);
        (await ListAsync(new ThreadListQueryDto { CreatorId = User })).Select(t => t.Id).ShouldBe([threadId]);
        _listed.ShouldBeEmpty("server code knows what it changed");
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Guid> SeedThreadAsync(ThreadListing listing, string title = "Thread")
    {
        if (!await DbContext.Set<CliAgentBinding>().AnyAsync())
        {
            await SeedBindingAsync();
        }

        var thread = new AgentThread { AgentId = AgentId, Title = title, Listing = listing, LastActivityTime = DateTime.UtcNow };
        DbContext.Set<AgentThread>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return thread.Id;
    }

    private async Task SeedQueuedRunAsync(Guid threadId)
    {
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, Status = CliRunStatus.Queued, Prompt = "running"
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task MarkRepliedAsync(Guid threadId)
    {
        using var scope = ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAgentThreadInternalService>().MarkRepliedAsync(threadId, DateTime.UtcNow);
    }

    /// <summary>A turn of the owner's through the chat entry point, as far as the run being queued.</summary>
    private async Task ChatAsync(Guid threadId, string text)
    {
        using var scope = ServiceProvider.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        await foreach (var evt in chat.ChatStreamingAsync(new ChatRequestDto { AgentId = AgentId, ThreadId = threadId, UserId = User, Message = text }))
        {
            if (evt.EventType == RunStreamEventTypes.RunStarted)
            {
                return;
            }
        }

        throw new InvalidOperationException("The turn did not start.");
    }

    private async Task<IList<AgentThreadDto>> ListAsync(ThreadListQueryDto query)
    {
        query.PageSize = 50;
        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentThreadService>().GetListAsync(query);
        return result.Data.ShouldNotBeNull().Items;
    }

    private async Task<ThreadAttentionDto> AttentionAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentThreadService>()
            .GetAttentionAsync(new ThreadAttentionQueryDto { CreatorId = User });
        return result.Data.ShouldNotBeNull();
    }

    private async Task<AgentThread> ReloadAsync(Guid threadId)
    {
        using var scope = ServiceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CliChatDbContext>().Set<AgentThread>()
            .AsNoTracking().IgnoreQueryFilters().FirstAsync(t => t.Id == threadId);
    }
}
