using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests.Events;

/// <summary>
/// 线程删除要带走该线程运行留下的 <see cref="AgentTask"/>（write_todos 的待办项）。
/// </summary>
/// <remarks>
/// AgentTask 只有裸 RunId 列、没有外键，数据库不会级联。处理器删掉运行而不删任务时，取自对话内容的任务标题
/// 留在 admin/ai/tasks 里，RunId 指向已不存在的运行。真实 SQLite + EFCoreRepository，不 mock 被测路径。
/// </remarks>
public class ThreadCleanupAgentTaskTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ThreadCleanupDbContext _context;

    public ThreadCleanupAgentTaskTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);

        _context = new ThreadCleanupDbContext(
            new DbContextOptionsBuilder<ThreadCleanupDbContext>().UseSqlite(_connection).Options, currentUser.Object);
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
    public async Task DeletingAThread_RemovesTheTasksOfItsRuns_AndLeavesOtherThreadsAlone()
    {
        var agent = new Agent { Name = "Planner", Provider = "MockProvider" };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();

        var deleted = new AgentThread { Title = "deleted", AgentId = agent.Id };
        var kept = new AgentThread { Title = "kept", AgentId = agent.Id };
        _context.Set<AgentThread>().AddRange(deleted, kept);
        await _context.SaveChangesAsync();

        var deletedRun = new AgentRun { AgentId = agent.Id, ThreadId = deleted.Id, Status = AgentRunStatus.Completed };
        var keptRun = new AgentRun { AgentId = agent.Id, ThreadId = kept.Id, Status = AgentRunStatus.Completed };
        _context.Set<AgentRun>().AddRange(deletedRun, keptRun);
        await _context.SaveChangesAsync();

        _context.Set<AgentTask>().AddRange(
            new AgentTask { RunId = deletedRun.Id, Title = "Email the landlord about the lease" },
            new AgentTask { RunId = deletedRun.Id, Title = "Draft the reply", OrderIndex = 1 },
            new AgentTask { RunId = keptRun.Id, Title = "Unrelated task" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var handler = new ThreadCleanupHandler(
            NullLogger<ThreadCleanupHandler>.Instance,
            runRepository: new EFCoreRepository<ThreadCleanupDbContext, AgentRun, Guid>(_context),
            nodeRepository: new EFCoreRepository<ThreadCleanupDbContext, AgentRunNode, Guid>(_context),
            taskRepository: new EFCoreRepository<ThreadCleanupDbContext, AgentTask, Guid>(_context));

        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = deleted.Id });

        var remaining = await _context.Set<AgentTask>().AsNoTracking().ToListAsync();
        remaining.Select(t => t.Title).ShouldBe(["Unrelated task"]);
        (await _context.Set<AgentRun>().AsNoTracking().Select(r => r.Id).ToListAsync()).ShouldBe([keptRun.Id]);
    }

    [Fact]
    public async Task WithoutATaskRepository_TheRestOfTheCleanupStillRuns()
    {
        var agent = new Agent { Name = "Planner", Provider = "MockProvider" };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();
        var thread = new AgentThread { Title = "t", AgentId = agent.Id };
        _context.Set<AgentThread>().Add(thread);
        await _context.SaveChangesAsync();
        _context.Set<AgentRun>().Add(new AgentRun { AgentId = agent.Id, ThreadId = thread.Id, Status = AgentRunStatus.Completed });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var handler = new ThreadCleanupHandler(
            NullLogger<ThreadCleanupHandler>.Instance,
            runRepository: new EFCoreRepository<ThreadCleanupDbContext, AgentRun, Guid>(_context));

        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = thread.Id });

        (await _context.Set<AgentRun>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }
}

internal sealed class ThreadCleanupDbContext : TnziDbContext<ThreadCleanupDbContext>
{
    public ThreadCleanupDbContext(DbContextOptions<ThreadCleanupDbContext> options, ICurrentUser currentUser)
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
        modelBuilder.ApplyConfiguration(new AgentTaskConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
