namespace Tnzi.EFCore.Tests;

/// <summary>
/// UnitOfWorkManager 嵌套事务语义测试。
/// 嵌套提交（depth > 1，如全局 UoW 包裹 ExecuteInUnitOfWorkAsync）的正确语义：
/// <b>flush 待保存变更（审计字段/ID 填充、事务内可见）但不提交物理事务</b>——
/// 物理提交/回滚由最外层（请求尾部全局 UnitOfWorkFilter）决定。
/// </summary>
public class UnitOfWorkManagerNestedTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly TestDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly SqliteConnection _connection;

    public UnitOfWorkManagerNestedTests()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.EnableSensitiveDataLogging();
        });

        // 让 UnitOfWorkManager 能发现测试 DbContext
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(TestDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestDbContext>();
        _manager = _serviceProvider.GetRequiredService<IUnitOfWorkManager>();

        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task NestedCommit_ShouldFlushPendingChanges_AndPopulateAuditFields()
    {
        // 模拟：全局 UoW（外层）+ ExecuteInUnitOfWorkAsync（内层）
        _manager.EnableTransaction(); // 外层（全局 filter）
        _manager.EnableTransaction(); // 内层（ExecuteInUnitOfWorkAsync）

        var user = new TestUser { UserName = "nested-flush", Email = "n@t.test" };
        _dbContext.Users.Add(user);

        // 内层提交：应 flush（审计字段填充、事务内可见），但不提交物理事务
        await _manager.CommitTransactionAsync();

        Assert.NotEqual(default, user.CreationTime); // 审计字段已填充（修复前为 default）
        Assert.NotEqual(Guid.Empty, user.Id);

        // 事务内已可见（从 DB 读，而非 change tracker）
        _dbContext.ChangeTracker.Clear();
        var visible = await _dbContext.Users.FindAsync(user.Id);
        Assert.NotNull(visible);

        // 外层提交：物理提交
        await _manager.CommitTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Users.FindAsync(user.Id);
        Assert.NotNull(persisted);
        Assert.Equal(0, _manager.TransactionDepth);
    }

    [Fact]
    public async Task NestedCommit_ShouldNotCommitPhysicalTransaction_OuterRollbackDiscardsAll()
    {
        _manager.EnableTransaction(); // 外层
        _manager.EnableTransaction(); // 内层

        var user = new TestUser { UserName = "nested-rollback", Email = "r@t.test" };
        _dbContext.Users.Add(user);

        // 内层提交（flush 进物理事务，但事务保持打开）
        await _manager.CommitTransactionAsync();

        // 外层决定回滚（如 action 返回非 2xx）——内层 flush 的数据必须被撤销
        await _manager.RollbackTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        var discarded = await _dbContext.Users.FindAsync(user.Id);
        Assert.Null(discarded);
        Assert.Equal(0, _manager.TransactionDepth);
    }

    [Fact]
    public async Task SequentialTransactions_InSameScope_BothPersist()
    {
        // 回归：同一 DI 作用域内**顺序**发生的两个独立事务。工作单元实例在作用域内复用，
        // 第一个事务提交后留下的 _hasCommitted 标志曾让第二次 CommitTransactionAsync
        // 直接早退——不 SaveChanges、不提交、不抛异常，第二段写入被静默丢弃。
        // 现实触发路径：一个服务顺序调用两个各自带事务的服务
        // （银行流水的「建单据 → 过账 → 确认匹配」）。
        _manager.EnableTransaction();
        _dbContext.Users.Add(new TestUser { UserName = "seq-1", Email = "s1@t.test" });
        await _manager.CommitTransactionAsync();
        Assert.Equal(0, _manager.TransactionDepth);

        _manager.EnableTransaction();
        _dbContext.Users.Add(new TestUser { UserName = "seq-2", Email = "s2@t.test" });
        await _manager.CommitTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        Assert.NotNull(await _dbContext.Users.FirstOrDefaultAsync(u => u.UserName == "seq-1"));
        Assert.NotNull(await _dbContext.Users.FirstOrDefaultAsync(u => u.UserName == "seq-2"));
        Assert.Equal(0, _manager.TransactionDepth);
    }

    [Fact]
    public async Task SecondTransaction_AfterCommittedFirst_CanStillRollBack()
    {
        // 同一毒化标志也让 RollbackTransactionAsync 早退：第一个事务提交后，
        // 第二个事务变得既提交不了也回滚不了。
        _manager.EnableTransaction();
        _dbContext.Users.Add(new TestUser { UserName = "keep-me", Email = "k@t.test" });
        await _manager.CommitTransactionAsync();

        _manager.EnableTransaction();
        _dbContext.Users.Add(new TestUser { UserName = "discard-me", Email = "d@t.test" });
        await _manager.RollbackTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        Assert.NotNull(await _dbContext.Users.FirstOrDefaultAsync(u => u.UserName == "keep-me"));
        Assert.Null(await _dbContext.Users.FirstOrDefaultAsync(u => u.UserName == "discard-me"));
    }

    [Fact]
    public async Task CommitAtEachLevel_ShouldOnlyCommitPhysicallyAtOutermost()
    {
        _manager.EnableTransaction();
        _manager.EnableTransaction();
        _manager.EnableTransaction(); // 三层嵌套

        _dbContext.Users.Add(new TestUser { UserName = "deep-1", Email = "d1@t.test" });
        await _manager.CommitTransactionAsync(); // 3 -> 2
        Assert.Equal(2, _manager.TransactionDepth);

        _dbContext.Users.Add(new TestUser { UserName = "deep-2", Email = "d2@t.test" });
        await _manager.CommitTransactionAsync(); // 2 -> 1
        Assert.Equal(1, _manager.TransactionDepth);

        await _manager.CommitTransactionAsync(); // 1 -> 0 物理提交
        Assert.Equal(0, _manager.TransactionDepth);

        _dbContext.ChangeTracker.Clear();
        Assert.Equal(2, await _dbContext.Users.CountAsync(u => u.UserName.StartsWith("deep-")));
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _connection?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
