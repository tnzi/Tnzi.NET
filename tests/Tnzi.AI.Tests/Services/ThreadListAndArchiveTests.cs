using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// The thread list filters by archive status and last activity and sorts on the server, so paging stays
/// correct; a thread can be archived and unarchived, and a new user message brings an archived thread back.
/// </summary>
/// <remarks>Real repositories on SQLite: the queries are what is under test.</remarks>
public class ThreadListAndArchiveTests : IntegratedTestBase<ThreadAttachmentDbContext>
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<ThreadAttachmentDbContext, Agent, Guid>>();
        services.AddScoped<IRepository<AgentThreadEntity, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentThreadEntity, Guid>>();
        services.AddScoped<IRepository<AgentThreadMessage, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentThreadMessage, Guid>>();
        services.AddScoped<AgentThreadService>();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Fact]
    public async Task Status_Active_LeavesArchivedThreadsOut_Archived_KeepsOnlyThem_AndAllIsTheDefault()
    {
        var active = await SeedAsync("active", activity: Now);
        var archived = await SeedAsync("archived", activity: Now, archivedAt: Now);

        (await ListAsync(new ThreadListQueryDto { Status = ThreadListStatus.Active })).ShouldBe([active]);
        (await ListAsync(new ThreadListQueryDto { Status = ThreadListStatus.Archived })).ShouldBe([archived]);
        (await ListAsync(new ThreadListQueryDto())).ShouldBe([active, archived], ignoreOrder: true);
    }

    [Fact]
    public async Task StartTime_KeepsThreadsActiveSinceThen()
    {
        var recent = await SeedAsync("recent", activity: Now.AddHours(-2));
        await SeedAsync("old", activity: Now.AddDays(-4));

        (await ListAsync(new ThreadListQueryDto { StartTime = Now.AddDays(-3) })).ShouldBe([recent]);
    }

    [Fact]
    public async Task AgentIds_ScopesTheListToThoseAgents_KeywordAndPagingStayInsideTheScope()
    {
        var chat = await SeedAgentAsync("chat");
        var code = await SeedAgentAsync("code");
        var other = await SeedAgentAsync("other");
        var c1 = await SeedAsync("fix parser", activity: Now, agentId: chat);
        var c2 = await SeedAsync("plan trip", activity: Now.AddHours(-1), agentId: chat);
        var k1 = await SeedAsync("fix build", activity: Now.AddHours(-2), agentId: code);
        var o1 = await SeedAsync("fix other", activity: Now.AddHours(-3), agentId: other);
        var bare = await SeedAsync("fix agentless", activity: Now.AddHours(-4));

        (await ListAsync(new ThreadListQueryDto { AgentIds = [chat] })).ShouldBe([c1, c2]);
        (await ListAsync(new ThreadListQueryDto { AgentIds = [chat, code] })).ShouldBe([c1, c2, k1]);
        (await ListAsync(new ThreadListQueryDto { AgentIds = [code, other], Keyword = "fix" })).ShouldBe([k1, o1]);
        (await ListAsync(new ThreadListQueryDto { AgentIds = [chat, code], PageIndex = 2, PageSize = 2 })).ShouldBe([k1]);
        (await Threads.GetListAsync(new ThreadListQueryDto { AgentIds = [chat, code], PageSize = 2 })).Data!.TotalCount.ShouldBe(3);

        // No scope (null or empty) is the old behaviour: every thread, agent-less ones included.
        (await ListAsync(new ThreadListQueryDto())).Count.ShouldBe(5);
        (await ListAsync(new ThreadListQueryDto { AgentIds = [] })).Count.ShouldBe(5);
        // AgentId and AgentIds are ANDed.
        (await ListAsync(new ThreadListQueryDto { AgentId = chat, AgentIds = [code] })).ShouldBeEmpty();
        bare.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Sort_LastActivity_CreationTime_AndTitle_IgnoringCase()
    {
        var b = await SeedAsync("banana", activity: Now.AddHours(-1), created: Now.AddDays(-1));
        var a = await SeedAsync("Apple", activity: Now.AddHours(-3), created: Now.AddHours(-1));
        var c = await SeedAsync("cherry", activity: Now, created: Now.AddDays(-2));

        (await ListAsync(new ThreadListQueryDto())).ShouldBe([c, b, a]);
        (await ListAsync(new ThreadListQueryDto { Sort = ThreadListSort.CreationTime })).ShouldBe([a, b, c]);
        (await ListAsync(new ThreadListQueryDto { Sort = ThreadListSort.Title })).ShouldBe([a, b, c]);
    }

    [Fact]
    public async Task Sorting_HappensBeforePaging()
    {
        var ids = new List<Guid>();
        foreach (var title in new[] { "d", "b", "e", "a", "c" })
        {
            ids.Add(await SeedAsync(title, activity: Now));
        }

        var first = await ListAsync(new ThreadListQueryDto { Sort = ThreadListSort.Title, PageIndex = 1, PageSize = 2 });
        var second = await ListAsync(new ThreadListQueryDto { Sort = ThreadListSort.Title, PageIndex = 2, PageSize = 2 });

        first.ShouldBe([ids[3], ids[1]]);
        second.ShouldBe([ids[4], ids[0]]);
    }

    [Fact]
    public async Task Archive_StampsTheTime_IsIdempotent_AndUnarchiveClearsIt()
    {
        var id = await SeedAsync("t", activity: Now);

        var archived = (await Threads.SetArchivedAsync(id, archived: true)).Data!;
        archived.ArchivedTime.ShouldNotBeNull();
        var stamp = archived.ArchivedTime;

        DbContext.ChangeTracker.Clear();
        (await Threads.SetArchivedAsync(id, archived: true)).Data!.ArchivedTime.ShouldBe(stamp);

        DbContext.ChangeTracker.Clear();
        (await Threads.SetArchivedAsync(id, archived: false)).Data!.ArchivedTime.ShouldBeNull();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<AgentThreadEntity>().SingleAsync(t => t.Id == id)).ArchivedTime.ShouldBeNull();
    }

    [Fact]
    public async Task Archive_UnknownThread_Is404()
    {
        var result = await Threads.SetArchivedAsync(Guid.NewGuid(), archived: true);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task AUserMessage_UnarchivesTheThread_AReplyDoesNot()
    {
        var id = await SeedAsync("t", activity: Now.AddDays(-1), archivedAt: Now.AddDays(-1));

        await Threads.SaveMessageAsync(id, MessageRole.Assistant, "late reply");
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<AgentThreadEntity>().SingleAsync(t => t.Id == id)).ArchivedTime.ShouldNotBeNull();

        await Threads.SaveMessageAsync(id, MessageRole.User, "picking this up again");
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<AgentThreadEntity>().SingleAsync(t => t.Id == id)).ArchivedTime.ShouldBeNull();
    }

    [Fact]
    public async Task ListAndDetail_CarryTheArchivedTime()
    {
        var id = await SeedAsync("t", activity: Now, archivedAt: Now);

        var listed = (await Threads.GetListAsync(new ThreadListQueryDto())).Data!.Items.Single();
        listed.ArchivedTime.ShouldBe(Now);
        (await Threads.GetDetailAsync(id)).Data!.ArchivedTime.ShouldBe(Now);
    }

    // ---------------------------------------------------------------- helpers

    private AgentThreadService Threads => ServiceProvider.GetRequiredService<AgentThreadService>();

    private async Task<List<Guid>> ListAsync(ThreadListQueryDto query)
    {
        var page = (await Threads.GetListAsync(query)).Data!;
        return page.Items.Select(t => t.Id).ToList();
    }

    private async Task<Guid> SeedAgentAsync(string name)
    {
        var agent = new Agent { Name = name, Provider = "OpenAI" };
        DbContext.Set<Agent>().Add(agent);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return agent.Id;
    }

    private async Task<Guid> SeedAsync(string title, DateTime activity, DateTime? created = null, DateTime? archivedAt = null, Guid? agentId = null)
    {
        var thread = new AgentThreadEntity { Title = title, LastActivityTime = activity, ArchivedTime = archivedAt, AgentId = agentId };
        DbContext.Set<AgentThreadEntity>().Add(thread);
        await DbContext.SaveChangesAsync();
        if (created.HasValue)
        {
            // 审计钩子在保存时写 CreationTime，按创建时间排序的用例要事后改写
            await DbContext.Set<AgentThreadEntity>().Where(t => t.Id == thread.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreationTime, created.Value));
        }

        DbContext.ChangeTracker.Clear();
        return thread.Id;
    }
}
