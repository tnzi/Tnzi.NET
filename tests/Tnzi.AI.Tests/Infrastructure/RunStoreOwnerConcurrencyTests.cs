using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// <see cref="RunStore.CountActiveRootRunsByOwnerAsync"/> 是顶层 spawn 并发上限的判据：
/// 只数归属人名下、没有父运行、仍在跑的运行。真实 SQLite + EFCoreRepository，让谓词经 EF Core 翻译。
/// </summary>
public class RunStoreOwnerConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgentRunFkDbContext _context;
    private readonly RunStore _store;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    public RunStoreOwnerConcurrencyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // 审计钩子会在 CreatorId 为 null 时填环境用户：这里没有环境用户，无主行才真的无主
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns((Guid?)null);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<AgentRunFkDbContext>().UseSqlite(_connection).Options;
        _context = new AgentRunFkDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();
        _store = new RunStore(
            new EFCoreRepository<AgentRunFkDbContext, AgentRun, Guid>(_context),
            new EFCoreRepository<AgentRunFkDbContext, AgentRunNode, Guid>(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<AgentRun> SeedAsync(Guid? creatorId, AgentRunStatus status, Guid? parentRunId = null)
    {
        var run = new AgentRun
        {
            CreatorId = creatorId,
            Status = status,
            ParentRunId = parentRunId,
            InputSummary = "seed"
        };
        _context.Set<AgentRun>().Add(run);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return run;
    }

    [Fact]
    public async Task CountsOnlyTheOwnersRootRunsThatAreStillRunning()
    {
        var root = await SeedAsync(_owner, AgentRunStatus.Running);
        await SeedAsync(_owner, AgentRunStatus.Pending);
        await SeedAsync(_owner, AgentRunStatus.Completed);          // finished: not counted
        await SeedAsync(_owner, AgentRunStatus.Failed);             // finished: not counted
        await SeedAsync(_owner, AgentRunStatus.Running, root.Id);   // in-tree child: bounded by the tree cap, not here
        await SeedAsync(_other, AgentRunStatus.Running);            // someone else's
        await SeedAsync(null, AgentRunStatus.Running);              // unowned

        (await _store.CountActiveRootRunsByOwnerAsync(_owner)).ShouldBe(2);
    }

    [Fact]
    public async Task NullOwner_CountsOnlyTheUnownedRoots()
    {
        await SeedAsync(null, AgentRunStatus.Running);
        await SeedAsync(null, AgentRunStatus.Completed);
        await SeedAsync(_owner, AgentRunStatus.Running);

        (await _store.CountActiveRootRunsByOwnerAsync(null)).ShouldBe(1);
    }
}
