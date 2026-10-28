using Microsoft.Data.Sqlite;
using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// Agent-bound 记忆写进去要读得回来：管理端新建、存储经 <see cref="MemoryScope.ForAgent"/> 写入的行，
/// 运行时按同一个范围读取与检索时必须命中。
/// </summary>
/// <remarks>
/// 写入方把 Scope 列写成完整键 <c>agent-bound:{agentId}:{name}</c>，而 <c>DatabaseMemoryStore</c> 按 Agent 读取时
/// 只认类别名本身（<c>Scope == "default"</c>），管理端「Agent 记忆」里新建的每一条都从未被注入过。
/// 旧的存储测试只用 <c>Scope = "default"</c> 的种子行，运行时测试又全部 mock 了存储，两头都看不见这个错位。
/// 真实 SQLite + EFCoreRepository。
/// </remarks>
public class AgentBoundMemoryRoundTripTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MemoryDedupDbContext _context;
    private readonly IRepository<MemoryEntry, Guid> _repository;
    private readonly IMemoryStore _store;

    private readonly Guid _agentId = Guid.NewGuid();

    public AgentBoundMemoryRoundTripTests()
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
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AgentMemoryService NewService(string defaultScope = "default")
    {
        var options = new AIOptions();
        options.ContextProviders.Memory.DefaultScope = defaultScope;
        return new AgentMemoryService(_repository, new ServiceCollection().AddLogging().BuildServiceProvider(),
            new StaticOptionsMonitor<AIOptions>(options));
    }

    [Fact]
    public async Task AdminCreatedMemory_IsReadBackByTheRuntimeScope()
    {
        (await NewService().CreateAsync(_agentId, new CreateAgentMemoryDto { Content = "Always greet by first name" })).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        var content = await _store.ReadAsync(MemoryScope.ForAgent(_agentId, "default"));

        content.ShouldNotBeNull();
        content.ShouldContain("Always greet by first name");
    }

    [Fact]
    public async Task AdminCreatedMemory_IsFoundByTheRuntimeSearch()
    {
        (await NewService().CreateAsync(_agentId, new CreateAgentMemoryDto { Content = "Refunds are processed within five business days" })).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        var results = await _store.SearchAsync(MemoryScope.ForAgent(_agentId, "default"), "refunds", maxResults: 10);

        results.ShouldContain(r => r.Content.Contains("five business days"));
    }

    [Fact]
    public async Task StoreWrittenAgentMemory_RoundTrips()
    {
        await _store.WriteAsync(MemoryScope.ForAgent(_agentId, "default"), "Curated persona note");
        _context.ChangeTracker.Clear();

        (await _store.ReadAsync(MemoryScope.ForAgent(_agentId, "default"))).ShouldNotBeNull().ShouldContain("Curated persona note");
    }

    [Fact]
    public async Task ConfiguredDefaultScope_IsUsedForAdminCreatedMemory()
    {
        (await NewService(defaultScope: "kb").CreateAsync(_agentId, new CreateAgentMemoryDto { Content = "Escalate billing disputes" })).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        // 运行时按配置的类别名加载（MemoryContributor：ForAgent(agentId, DefaultScope)）
        (await _store.ReadAsync(MemoryScope.ForAgent(_agentId, "kb"))).ShouldNotBeNull().ShouldContain("Escalate billing disputes");
    }

    [Fact]
    public async Task LegacyRowsKeyedOnTheBareName_AreStillRead()
    {
        _context.Set<MemoryEntry>().Add(new MemoryEntry { Scope = "default", Content = "legacy agent row", AgentId = _agentId });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        (await _store.ReadAsync(MemoryScope.ForAgent(_agentId, "default"))).ShouldNotBeNull().ShouldContain("legacy agent row");
    }

    [Fact]
    public async Task AnotherAgentsMemory_IsNotRead()
    {
        (await NewService().CreateAsync(Guid.NewGuid(), new CreateAgentMemoryDto { Content = "other agent fact" })).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        (await _store.ReadAsync(MemoryScope.ForAgent(_agentId, "default"))).ShouldBeNull();
    }
}
