using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests;

/// <summary>
/// AgentRun.AgentId/ThreadId FK 集成测试。
/// 验证引用完整性 + "Run 是审计记录绝不能被级联删除" 的删除语义：
/// - AgentId → Agent: SET NULL（物理删除 Agent 时置空引用、保留运行记录）；
/// - ThreadId → AgentThread: RESTRICT（仍有运行记录引用时不允许物理删除 Thread，
///   同时避免 SQL Server 的 multiple-cascade-paths 菱形）。
/// 使用 SQLite 内存库 + 自管 DbContext（镜像 AgentProviderFkTests 的自包含模式）。
/// </summary>
public class AgentRunFkTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgentRunFkDbContext _context;

    public AgentRunFkTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<AgentRunFkDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AgentRunFkDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(Agent Agent, AgentThread Thread)> SeedAgentAndThreadAsync()
    {
        var agent = new Agent { Name = "FK Run Agent", Provider = "MockProvider" };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();

        var thread = new AgentThread { Title = "FK Run Thread", AgentId = agent.Id };
        _context.Set<AgentThread>().Add(thread);
        await _context.SaveChangesAsync();

        return (agent, thread);
    }

    [Fact]
    public async Task AgentRun_WithValidReferences_Persists()
    {
        var (agent, thread) = await SeedAgentAndThreadAsync();

        var run = new AgentRun
        {
            AgentId = agent.Id,
            ThreadId = thread.Id,
            Status = AgentRunStatus.Completed,
            InputSummary = "hello"
        };
        _context.Set<AgentRun>().Add(run);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var loaded = await _context.Set<AgentRun>().FirstAsync(r => r.Id == run.Id);
        loaded.AgentId.ShouldBe(agent.Id);
        loaded.ThreadId.ShouldBe(thread.Id);
    }

    [Fact]
    public async Task AgentRun_WithNonExistentAgentId_IsRejected()
    {
        // 引用完整性：裸 Guid 不再可写入 - FK 约束拒绝悬挂引用
        var run = new AgentRun
        {
            AgentId = Guid.NewGuid(), // 不存在的 Agent
            Status = AgentRunStatus.Pending,
            InputSummary = "dangling"
        };
        _context.Set<AgentRun>().Add(run);

        await Should.ThrowAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task HardDeletingAgent_SetsRunAgentIdNull_RunSurvives()
    {
        var (agent, thread) = await SeedAgentAndThreadAsync();

        var run = new AgentRun
        {
            AgentId = agent.Id,
            ThreadId = thread.Id,
            Status = AgentRunStatus.Completed,
            InputSummary = "audit record"
        };
        _context.Set<AgentRun>().Add(run);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // 物理删除 Agent。注意必须走 ExecuteDelete（原生 SQL）：TnziDbContext 的 SaveChanges
        // 会把 ISoftDelete 实体的 Remove 转为软删除，永远不会触发 DB 级 FK 动作。
        // DB 级 SET NULL：Thread.AgentId 与 Run.AgentId 均应被置空。
        await _context.Set<Agent>().Where(a => a.Id == agent.Id).ExecuteDeleteAsync();
        _context.ChangeTracker.Clear();

        // Run 行存活、AgentId 被置空（审计记录不随 Agent 删除）
        var loadedRun = await _context.Set<AgentRun>().FirstAsync(r => r.Id == run.Id);
        loadedRun.AgentId.ShouldBeNull();
        loadedRun.ThreadId.ShouldBe(thread.Id);
        loadedRun.InputSummary.ShouldBe("audit record");
    }

    [Fact]
    public async Task HardDeletingThread_WithRuns_IsRestricted()
    {
        var (agent, thread) = await SeedAgentAndThreadAsync();

        var run = new AgentRun
        {
            AgentId = agent.Id,
            ThreadId = thread.Id,
            Status = AgentRunStatus.Completed,
            InputSummary = "audit record"
        };
        _context.Set<AgentRun>().Add(run);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // RESTRICT：仍有 Run 引用时不允许物理删除 Thread（审计保护）。
        // ExecuteDelete 绕过 SaveChanges 的软删除转换 → 真正命中 DB FK 约束（SqliteException : DbException）。
        await Should.ThrowAsync<DbException>(
            () => _context.Set<AgentThread>().Where(t => t.Id == thread.Id).ExecuteDeleteAsync());

        // Run 与 Thread 均完好存活
        (await _context.Set<AgentRun>().AnyAsync(r => r.Id == run.Id)).ShouldBeTrue();
        (await _context.Set<AgentThread>().AnyAsync(t => t.Id == thread.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task HardDeletingThread_AfterRunsCleanedUp_Succeeds()
    {
        var (agent, thread) = await SeedAgentAndThreadAsync();

        var run = new AgentRun
        {
            AgentId = agent.Id,
            ThreadId = thread.Id,
            Status = AgentRunStatus.Completed,
            InputSummary = "to clean"
        };
        _context.Set<AgentRun>().Add(run);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // 镜像 ThreadCleanupHandler 的次序：先清理 Run，再物理删 Thread → 可删。
        await _context.Set<AgentRun>().Where(r => r.ThreadId == thread.Id).ExecuteDeleteAsync();
        await _context.Set<AgentThread>().Where(t => t.Id == thread.Id).ExecuteDeleteAsync();

        (await _context.Set<AgentThread>().IgnoreQueryFilters().AnyAsync(t => t.Id == thread.Id)).ShouldBeFalse();
    }
}

/// <summary>
/// 测试专用 DbContext - 注册 AgentRun FK 测试所需的实体集
/// （Agent 配置含指向 Provider 的 FK，须一并注册）。
/// </summary>
internal sealed class AgentRunFkDbContext : TnziDbContext<AgentRunFkDbContext>
{
    public AgentRunFkDbContext(
        DbContextOptions<AgentRunFkDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRunNodeConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
