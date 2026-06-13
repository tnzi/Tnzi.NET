using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests;

/// <summary>
/// Agent.ProviderId FK 集成测试（过渡期）。
/// 验证 Agent 与 Provider 之间的可选外键导航能正确持久化并加载。
/// 使用 SQLite 内存库 + 自管 DbContext（镜像 EntityPolicyTests 的自包含模式），
/// 因为 FK 导航加载必须经过真实的 EF Core 查询管道。
/// </summary>
public class AgentProviderFkTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgentProviderFkDbContext _context;

    public AgentProviderFkTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<AgentProviderFkDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AgentProviderFkDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Agent_WithProviderId_LoadsProviderNavigation()
    {
        // Arrange: 持久化一个 Provider 和一个引用它的 Agent
        var provider = new Provider
        {
            Name = "openai-prod",
            ProviderType = "OpenAI",
            DefaultModel = "gpt-4o",
            Scope = ResourceScope.System
        };
        _context.Set<Provider>().Add(provider);
        await _context.SaveChangesAsync();

        var agent = new Agent
        {
            Name = "FK Agent",
            Provider = "openai-prod", // 遗留字符串解析键仍然保留
            ProviderId = provider.Id, // 新的权威 FK 引用
            Model = "gpt-4o"
        };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();

        // 清除追踪缓存，强制从数据库重新加载（证明 FK 真实持久化）
        _context.ChangeTracker.Clear();

        // Act: 加载 Agent 并 Include 其 Provider 导航
        var loaded = await _context.Set<Agent>()
            .Include(a => a.ProviderEntity)
            .FirstAsync(a => a.Id == agent.Id);

        // Assert: ProviderId 已持久化，导航属性被填充
        loaded.ProviderId.ShouldBe(provider.Id);
        loaded.ProviderEntity.ShouldNotBeNull();
        loaded.ProviderEntity!.Name.ShouldBe("openai-prod");
        loaded.ProviderEntity.ProviderType.ShouldBe("OpenAI");

        // 遗留字符串字段不受影响
        loaded.Provider.ShouldBe("openai-prod");
    }

    [Fact]
    public async Task Agent_WithoutProviderId_HasNullNavigation()
    {
        // Arrange: Agent 不设置 ProviderId（过渡期允许 null）
        var agent = new Agent
        {
            Name = "Legacy Agent",
            Provider = "MockProvider"
        };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act
        var loaded = await _context.Set<Agent>()
            .Include(a => a.ProviderEntity)
            .FirstAsync(a => a.Id == agent.Id);

        // Assert: 无 FK 时导航为 null，不影响遗留字符串字段
        loaded.ProviderId.ShouldBeNull();
        loaded.ProviderEntity.ShouldBeNull();
        loaded.Provider.ShouldBe("MockProvider");
    }

    [Fact]
    public async Task DeletingProvider_SetsAgentProviderIdToNull()
    {
        // Arrange: 验证 OnDelete(SetNull) 行为 — 删除 Provider 不级联删除 Agent
        var provider = new Provider
        {
            Name = "to-delete",
            ProviderType = "OpenAI",
            Scope = ResourceScope.System
        };
        _context.Set<Provider>().Add(provider);
        await _context.SaveChangesAsync();

        var agent = new Agent
        {
            Name = "Orphan Agent",
            Provider = "to-delete",
            ProviderId = provider.Id
        };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();

        // Act: 删除 Provider
        _context.Set<Provider>().Remove(provider);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Assert: Agent 仍存在，ProviderId 被置空（SetNull）
        var loaded = await _context.Set<Agent>().FirstAsync(a => a.Id == agent.Id);
        loaded.ShouldNotBeNull();
        loaded.ProviderId.ShouldBeNull();
    }
}

/// <summary>
/// 测试专用 DbContext — 仅注册 FK 测试所需的 Agent + Provider 实体集。
/// </summary>
internal sealed class AgentProviderFkDbContext : TnziDbContext<AgentProviderFkDbContext>
{
    public AgentProviderFkDbContext(
        DbContextOptions<AgentProviderFkDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
