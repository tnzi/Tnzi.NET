using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// The thread list's order: threads awaiting their owner, then running ones, then the rest; inside every tier the
/// owner's last message (creation when there is none), newer creation, then id. Only the owner's own messages move a
/// row; the order is applied in the query, so paging never pushes an awaiting or running thread to a later page.
/// </summary>
/// <remarks>Real repositories on SQLite; which threads await their owner and which run come from stand-ins.</remarks>
public class ThreadListOrderTests : IntegratedTestBase<ThreadAttachmentDbContext>
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeAwaitingOwner _awaiting = new();
    private readonly FakeAwaitingOwner _appAwaiting = new();
    private IReadOnlyCollection<Guid> _running = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<ThreadAttachmentDbContext, Agent, Guid>>();
        services.AddScoped<IRepository<AgentThreadEntity, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentThreadEntity, Guid>>();
        services.AddScoped<IRepository<AgentThreadMessage, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentThreadMessage, Guid>>();
        services.AddScoped<AgentThreadService>();
        services.AddSingleton<IThreadAwaitingOwnerProvider>(_awaiting);
        services.AddSingleton<IThreadAwaitingOwnerProvider>(_appAwaiting);
        // Only which threads run is asked of the dispatcher
        var dispatcher = new Mock<ICliAgentDispatcher>();
        dispatcher.Setup(d => d.GetRunningThreadIdsAsSystemAsync(It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid>? ids, CancellationToken _) =>
                (ids is null ? _running : _running.Where(ids.Contains)).ToHashSet());
        services.AddSingleton(dispatcher.Object);

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Fact]
    public async Task AwaitingOwner_ThenRunning_ThenTheRest_EachByTheOwnersLastMessage()
    {
        var fresh = await SeedAsync("fresh", userMessage: Now);
        var older = await SeedAsync("older", userMessage: Now.AddHours(-1));
        var runningOld = await SeedAsync("running old", userMessage: Now.AddDays(-3));
        var runningNew = await SeedAsync("running new", userMessage: Now.AddDays(-2));
        var awaitingOld = await SeedAsync("awaiting old", userMessage: Now.AddDays(-9));
        var awaitingApp = await SeedAsync("awaiting app", userMessage: Now.AddDays(-5));

        _awaiting.Ids = [awaitingOld];
        _appAwaiting.Ids = [awaitingApp];
        _running = [runningOld, runningNew, awaitingOld];

        var list = (await Threads.GetListAsync(new ThreadListQueryDto())).Data!.Items;

        list.Select(t => t.Id).ShouldBe([awaitingApp, awaitingOld, runningNew, runningOld, fresh, older]);
        list.Single(t => t.Id == awaitingOld).AwaitingOwner.ShouldBeTrue();
        list.Single(t => t.Id == awaitingOld).Running.ShouldBeTrue("the flags are independent; the tier takes the higher one");
        list.Single(t => t.Id == runningNew).AwaitingOwner.ShouldBeFalse();
        list.Single(t => t.Id == fresh).LastUserMessageTime.ShouldBe(Now);
    }

    [Fact]
    public async Task NoOwnerMessage_FallsBackToCreation_TiesGoToNewerCreation_ThenId()
    {
        var spoke = await SeedAsync("spoke", userMessage: Now.AddHours(-2), created: Now.AddDays(-1));
        var silentNew = await SeedAsync("silent, created recently", created: Now.AddHours(-1));
        var tieOld = await SeedAsync("tie, older", userMessage: Now.AddHours(-3), created: Now.AddDays(-5));
        var tieNew = await SeedAsync("tie, newer", userMessage: Now.AddHours(-3), created: Now.AddDays(-4));

        (await ListAsync(new ThreadListQueryDto())).ShouldBe([silentNew, spoke, tieNew, tieOld]);
    }

    [Fact]
    public async Task OnlyTheOwnersMessages_MoveARow()
    {
        var a = await SeedAsync("a", userMessage: Now.AddHours(-2));
        var b = await SeedAsync("b", userMessage: Now.AddHours(-1));
        (await ListAsync(new ThreadListQueryDto())).ShouldBe([b, a]);

        // The AI answers in a, a is renamed, read and archived and unarchived: nothing of it is the owner speaking
        await Threads.SaveMessageAsync(a, MessageRole.Assistant, "a reply");
        await Threads.SaveMessageAsync(a, MessageRole.Tool, "a tool result");
        DbContext.ChangeTracker.Clear();
        await Threads.UpdateTitleAsync(a, "renamed");
        DbContext.ChangeTracker.Clear();
        await Threads.MarkReadAsync(a);
        await Threads.MarkRepliedAsync(a, DateTime.UtcNow);
        (await ListAsync(new ThreadListQueryDto())).ShouldBe([b, a]);

        // The owner speaks in a: it goes to the top
        await Threads.SaveMessageAsync(a, MessageRole.User, "and another thing");
        (await ListAsync(new ThreadListQueryDto())).ShouldBe([a, b]);
    }

    [Fact]
    public async Task TheOwnersMessageTime_OnlyMovesForward_AndAQueuedMessageKeepsItsTime()
    {
        var id = await SeedAsync("t", userMessage: Now);

        // A queued message is marked when it is queued; when it is delivered later it is written with the time it was queued
        await Threads.MarkUserMessageAsync(id, Now.AddMinutes(5));
        await Threads.SaveUserMessageAsync(id, "queued earlier", metadata: null, sentAt: Now.AddMinutes(5));
        (await ReloadAsync(id)).LastUserMessageTime!.Value.ShouldBe(Now.AddMinutes(5), TimeSpan.FromMilliseconds(1));

        // An older time never takes it back
        await Threads.MarkUserMessageAsync(id, Now.AddMinutes(-30));
        await Threads.SaveUserMessageAsync(id, "late delivery of an old one", metadata: null, sentAt: Now.AddMinutes(-20));
        (await ReloadAsync(id)).LastUserMessageTime!.Value.ShouldBe(Now.AddMinutes(5), TimeSpan.FromMilliseconds(1));

        // A message sent now (no time given) is stamped now
        await Threads.SaveMessageAsync(id, MessageRole.User, "now");
        (await ReloadAsync(id)).LastUserMessageTime!.Value.ShouldBeGreaterThan(Now.AddMinutes(5));
    }

    [Fact]
    public async Task Paging_KeepsEveryAwaitingAndRunningThreadOnTheFirstPage()
    {
        var recent = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            recent.Add(await SeedAsync($"recent {i}", userMessage: Now.AddMinutes(-i)));
        }

        var awaiting = await SeedAsync("awaiting, long ago", userMessage: Now.AddDays(-60));
        var running = await SeedAsync("running, long ago", userMessage: Now.AddDays(-30));
        _awaiting.Ids = [awaiting];
        _running = [running];

        var first = (await Threads.GetListAsync(new ThreadListQueryDto { PageIndex = 1, PageSize = 3 })).Data!;
        var second = await ListAsync(new ThreadListQueryDto { PageIndex = 2, PageSize = 3 });
        var third = await ListAsync(new ThreadListQueryDto { PageIndex = 3, PageSize = 3 });

        first.Items.Select(t => t.Id).ShouldBe([awaiting, running, recent[0]]);
        first.TotalCount.ShouldBe(7);
        second.ShouldBe([recent[1], recent[2], recent[3]]);
        third.ShouldBe([recent[4]]);
    }

    [Fact]
    public async Task EverySort_KeepsTheTiers_AndOrdersInsideThem()
    {
        var banana = await SeedAsync("banana", userMessage: Now, created: Now.AddDays(-1));
        var apple = await SeedAsync("Apple", userMessage: Now.AddHours(-1), created: Now.AddHours(-1));
        var cherry = await SeedAsync("cherry", userMessage: Now.AddDays(-9), created: Now.AddDays(-9));
        var date = await SeedAsync("date", userMessage: Now.AddDays(-8), created: Now.AddDays(-2));
        _awaiting.Ids = [cherry];

        (await ListAsync(new ThreadListQueryDto { Sort = ThreadListSort.Title })).ShouldBe([cherry, apple, banana, date]);
        (await ListAsync(new ThreadListQueryDto { Sort = ThreadListSort.CreationTime })).ShouldBe([cherry, apple, banana, date]);
        (await ListAsync(new ThreadListQueryDto { Sort = ThreadListSort.LastUserMessage })).ShouldBe([cherry, banana, apple, date]);
    }

    [Fact]
    public async Task TheOwnerFilter_IsHandedToTheProviders_AndOnlyMatchingThreadsAreMarked()
    {
        var owner = Guid.NewGuid();
        var mine = await SeedAsync("mine", userMessage: Now.AddDays(-2), creator: owner);
        var newer = await SeedAsync("newer", userMessage: Now, creator: owner);
        var someoneElses = await SeedAsync("someone else's", userMessage: Now, creator: Guid.NewGuid());
        _awaiting.Ids = [mine, someoneElses];

        (await ListAsync(new ThreadListQueryDto { CreatorId = owner })).ShouldBe([mine, newer]);
        _awaiting.LastQuery!.OwnerId.ShouldBe(owner);
        _awaiting.LastQuery.ThreadIds.ShouldBeNull();
    }

    [Fact]
    public async Task AProviderThatFails_IsLeftOut_AndTheListStillAnswers()
    {
        var old = await SeedAsync("old", userMessage: Now.AddDays(-1));
        var fresh = await SeedAsync("fresh", userMessage: Now);
        _awaiting.Throws = true;
        _appAwaiting.Ids = [old];

        (await ListAsync(new ThreadListQueryDto())).ShouldBe([old, fresh]);
    }

    [Fact]
    public async Task TheActivityWindow_FiltersOnTheSameTimeTheListIsOrderedBy()
    {
        var spoke = await SeedAsync("spoke recently", userMessage: Now.AddHours(-1), created: Now.AddDays(-10));
        await SeedAsync("only replied to recently", userMessage: Now.AddDays(-5), created: Now.AddDays(-10));
        var created = await SeedAsync("created recently, no message", created: Now.AddHours(-2));

        (await ListAsync(new ThreadListQueryDto { StartTime = Now.AddDays(-1) })).ShouldBe([spoke, created]);
    }

    [Fact]
    public async Task DetailAndSingleRead_CarryTheFlagAndTheTime_AndAttentionListsAwaitingThreadsInListOrder()
    {
        var owner = Guid.NewGuid();
        var older = await SeedAsync("older", userMessage: Now.AddDays(-2), creator: owner);
        var newer = await SeedAsync("newer", userMessage: Now.AddDays(-1), creator: owner);
        var calm = await SeedAsync("calm", userMessage: Now, creator: owner);
        _awaiting.Ids = [older, newer];

        var detail = (await Threads.GetDetailAsync(older)).Data!;
        detail.AwaitingOwner.ShouldBeTrue();
        detail.LastUserMessageTime.ShouldBe(Now.AddDays(-2));
        _awaiting.LastQuery!.ThreadIds.ShouldBe([older]);
        (await Threads.GetByIdAsync(calm)).Data!.AwaitingOwner.ShouldBeFalse();

        var attention = (await Threads.GetAttentionAsync(new ThreadAttentionQueryDto { CreatorId = owner })).Data!;
        attention.AwaitingOwnerCount.ShouldBe(2);
        attention.AwaitingOwnerThreadIds.ShouldBe([newer, older]);
    }

    // ---------------------------------------------------------------- helpers

    private AgentThreadService Threads => ServiceProvider.GetRequiredService<AgentThreadService>();

    private async Task<List<Guid>> ListAsync(ThreadListQueryDto query)
    {
        var page = (await Threads.GetListAsync(query)).Data!;
        return page.Items.Select(t => t.Id).ToList();
    }

    private async Task<AgentThreadEntity> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<AgentThreadEntity>().AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task<Guid> SeedAsync(string title, DateTime? userMessage = null, DateTime? created = null, Guid? creator = null)
    {
        var thread = new AgentThreadEntity { Title = title, LastActivityTime = Now, LastUserMessageTime = userMessage, CreatorId = creator };
        DbContext.Set<AgentThreadEntity>().Add(thread);
        await DbContext.SaveChangesAsync();
        // 审计钩子在保存时写 CreationTime（与创建者），排序用例要事后改写
        await DbContext.Set<AgentThreadEntity>().Where(t => t.Id == thread.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.CreationTime, created ?? Now.AddDays(-30))
                .SetProperty(t => t.CreatorId, creator));

        DbContext.ChangeTracker.Clear();
        return thread.Id;
    }

    private sealed class FakeAwaitingOwner : IThreadAwaitingOwnerProvider
    {
        public IReadOnlyCollection<Guid> Ids { get; set; } = [];
        public bool Throws { get; set; }
        public ThreadAwaitingOwnerQuery? LastQuery { get; private set; }

        public Task<IReadOnlyCollection<Guid>> GetThreadIdsAsync(ThreadAwaitingOwnerQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            if (Throws)
            {
                throw new InvalidOperationException("the confirmations store is down");
            }

            IReadOnlyCollection<Guid> ids = query.ThreadIds is null ? Ids : Ids.Where(query.ThreadIds.Contains).ToList();
            return Task.FromResult(ids);
        }
    }
}
