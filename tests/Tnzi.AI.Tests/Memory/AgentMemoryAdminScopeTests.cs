using Microsoft.Data.Sqlite;
using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// 管理端 Agent 记忆只管 agent-bound 那一段（管理员为 Agent 预置的记忆），不能碰用户在对话里沉淀的个人记忆。
/// </summary>
/// <remarks>
/// 开启 Agent 隔离（<c>EnableAgentIsolation</c>，可在配置中心热开）后，用户与 Agent X 对话沉淀的个人记忆行
/// 同样带 <c>AgentId = X</c>（外加 <c>UserId</c>）。只按 AgentId 过滤时，持 <c>ai.agent.view</c> 的管理员
/// 能在该 Agent 的记忆页看到每个用户的私有记忆，持 <c>ai.agent.update</c> 的能改能删，而 DTO 不带 UserId，
/// 管理员连这是谁的都分不出来。真实 SQLite + EFCoreRepository。
/// </remarks>
public class AgentMemoryAdminScopeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MemoryDedupDbContext _context;
    private readonly IRepository<MemoryEntry, Guid> _repository;
    private readonly DatabaseMemoryStore _store;
    private readonly AgentMemoryService _service;

    private readonly Guid _agentId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public AgentMemoryAdminScopeTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);

        _context = new MemoryDedupDbContext(
            new DbContextOptionsBuilder<MemoryDedupDbContext>().UseSqlite(_connection).Options,
            currentUser.Object);
        _context.Database.EnsureCreated();

        _repository = new EFCoreRepository<MemoryDedupDbContext, MemoryEntry, Guid>(_context);
        _store = new DatabaseMemoryStore(_repository, Mock.Of<ILogger<DatabaseMemoryStore>>());

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _service = new AgentMemoryService(_repository, new ServiceCollection().AddLogging().BuildServiceProvider());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task List_ReturnsOnlyAgentBoundEntries_NotUsersPrivateMemories()
    {
        var curated = await SeedCuratedAsync("Always greet by first name");
        var personal = await SeedUserMemoryAsync("My home address is 12 Elm Street");

        var result = await _service.GetListAsync(_agentId, new AgentMemoryListQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue(result.Message);
        var ids = result.Data!.Items.Select(i => i.Id).ToList();
        ids.ShouldContain(curated);
        ids.ShouldNotContain(personal, "a user's private memory is not the agent's curated memory");
        result.Data.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Update_OnAUsersPrivateMemory_Returns404AndLeavesItIntact()
    {
        var personal = await SeedUserMemoryAsync("My home address is 12 Elm Street");

        var result = await _service.UpdateAsync(_agentId, personal, new UpdateAgentMemoryDto { Content = "overwritten by an admin" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        _context.ChangeTracker.Clear();
        (await _context.Set<MemoryEntry>().SingleAsync(e => e.Id == personal)).Content.ShouldBe("My home address is 12 Elm Street");
    }

    [Fact]
    public async Task Delete_OnAUsersPrivateMemory_Returns404AndLeavesItIntact()
    {
        var personal = await SeedUserMemoryAsync("My home address is 12 Elm Street");

        var result = await _service.DeleteAsync(_agentId, personal);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        _context.ChangeTracker.Clear();
        (await _context.Set<MemoryEntry>().AnyAsync(e => e.Id == personal)).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAndDelete_OnAnAgentBoundEntry_StillWork()
    {
        var curated = await SeedCuratedAsync("Answer in English");

        (await _service.UpdateAsync(_agentId, curated, new UpdateAgentMemoryDto { Content = "Answer in French" })).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();
        (await _service.DeleteAsync(_agentId, curated)).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();
        (await _context.Set<MemoryEntry>().AnyAsync(e => e.Id == curated)).ShouldBeFalse();
    }

    [Fact]
    public async Task AnotherAgentsCuratedEntry_IsNotReachableThroughThisAgent()
    {
        var otherAgent = Guid.NewGuid();
        var created = await _service.CreateAsync(otherAgent, new CreateAgentMemoryDto { Content = "other agent fact" });
        _context.ChangeTracker.Clear();

        (await _service.DeleteAsync(_agentId, created.Data!.Id)).Code.ShouldBe(404);
    }

    private async Task<Guid> SeedCuratedAsync(string content)
    {
        var created = await _service.CreateAsync(_agentId, new CreateAgentMemoryDto { Content = content });
        created.Succeeded.ShouldBeTrue(created.Message);
        _context.ChangeTracker.Clear();
        return created.Data!.Id;
    }

    /// <summary>运行时开启用户 + Agent 隔离时 save_memory / AutoPersist 写下的那种行。</summary>
    private async Task<Guid> SeedUserMemoryAsync(string content)
    {
        var scope = MemoryScopeResolver.BuildLocalScope("default", enableUserIsolation: true, enableAgentIsolation: true, _userId, _agentId);
        await _store.AppendAsync(scope, content);
        _context.ChangeTracker.Clear();

        var row = await _context.Set<MemoryEntry>().SingleAsync(e => e.Content == content);
        row.AgentId.ShouldBe(_agentId, "precondition: the user's memory carries the agent id");
        row.UserId.ShouldBe(_userId);
        _context.ChangeTracker.Clear();
        return row.Id;
    }
}
