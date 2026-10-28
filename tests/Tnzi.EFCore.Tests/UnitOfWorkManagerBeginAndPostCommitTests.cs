namespace Tnzi.EFCore.Tests;

/// <summary>
/// <see cref="UnitOfWorkManager"/> 的三处边界：
/// <c>BeginTransactionAsync</c> 在已有事务里必须计入嵌套深度（否则与它配对的提交会把外层事务提前物理提交）；
/// 提交后动作拿到的令牌不是调用方的（事务已提交，客户端断开不该让副作用被取消而丢失）；
/// 内层回滚终结了环境事务时要留下警告，而不是让外层的提交安静地空转。
/// </summary>
public class UnitOfWorkManagerBeginAndPostCommitTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly TestDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly IPostCommitActionQueue _queue;
    private readonly SqliteConnection _connection;
    private readonly CapturingLoggerProvider _logs = new();

    public UnitOfWorkManagerBeginAndPostCommitTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(_logs));
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(TestDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<TestDbContext>();
        _manager = _scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        _queue = _scope.ServiceProvider.GetRequiredService<IPostCommitActionQueue>();

        _dbContext.Database.EnsureCreated();
    }

    private async Task<bool> PersistedAsync(string userName)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Users.AnyAsync(u => u.UserName == userName);
    }

    // ---- BeginTransactionAsync 的嵌套语义 ----

    [Fact]
    public async Task Begin_InsideAnEnabledTransaction_NestsAndLeavesTheOuterInCharge()
    {
        var postCommitRuns = 0;

        _manager.EnableTransaction(); // 外层（全局工作单元过滤器）
        await _manager.BeginTransactionAsync(); // 内层（Begin / Commit / Rollback 三件套的调用方）
        Assert.Equal(2, _manager.TransactionDepth);

        _dbContext.Users.Add(new TestUser { UserName = "begin-nested", Email = "bn@t.test" });
        _queue.Enqueue(_ => { postCommitRuns++; return Task.CompletedTask; });

        await _manager.CommitTransactionAsync(); // 内层提交：只 flush

        Assert.Equal(1, _manager.TransactionDepth);
        Assert.True(_manager.IsEnabledTransaction);
        Assert.Equal(0, postCommitRuns);

        await _manager.RollbackTransactionAsync(); // 外层决定回滚：内层写入必须一起撤销

        Assert.False(await PersistedAsync("begin-nested"));
        Assert.Equal(0, postCommitRuns);
    }

    [Fact]
    public async Task Begin_InsideAnEnabledTransaction_OuterCommitPersistsAndRunsPostCommitOnce()
    {
        var postCommitRuns = 0;

        _manager.EnableTransaction();
        await _manager.BeginTransactionAsync();
        _dbContext.Users.Add(new TestUser { UserName = "begin-outer-commit", Email = "boc@t.test" });
        _queue.Enqueue(_ => { postCommitRuns++; return Task.CompletedTask; });

        await _manager.CommitTransactionAsync();
        await _manager.CommitTransactionAsync();

        Assert.Equal(0, _manager.TransactionDepth);
        Assert.True(await PersistedAsync("begin-outer-commit"));
        Assert.Equal(1, postCommitRuns);
    }

    [Fact]
    public async Task Begin_WithoutAnOuterTransaction_CommitsOnItsOwn()
    {
        await _manager.BeginTransactionAsync();
        Assert.Equal(1, _manager.TransactionDepth);

        _dbContext.Users.Add(new TestUser { UserName = "begin-alone", Email = "ba@t.test" });
        await _manager.CommitTransactionAsync();

        Assert.Equal(0, _manager.TransactionDepth);
        Assert.True(await PersistedAsync("begin-alone"));
    }

    // ---- 提交后动作的令牌 ----

    [Fact]
    public async Task PostCommitActions_KeepRunning_WhenTheCallersTokenIsCancelledAfterCommit()
    {
        using var requestAborted = new CancellationTokenSource();
        var laterActionRan = false;

        _manager.EnableTransaction();
        _dbContext.Users.Add(new TestUser { UserName = "post-commit-token", Email = "pct@t.test" });

        // 第一个动作模拟「提交之后客户端断开」：调用方的令牌在提交后、其余动作执行前被取消
        _queue.Enqueue(_ => { requestAborted.Cancel(); return Task.CompletedTask; });
        _queue.Enqueue(ct =>
        {
            ct.ThrowIfCancellationRequested();
            laterActionRan = true;
            return Task.CompletedTask;
        });

        await _manager.CommitTransactionAsync(requestAborted.Token);

        Assert.True(await PersistedAsync("post-commit-token"));
        Assert.True(laterActionRan);
    }

    // ---- 内层回滚终结环境事务 ----

    [Fact]
    public async Task NestedRollback_ThenOuterCommit_IsLoggedInsteadOfSilentlyDoingNothing()
    {
        _manager.EnableTransaction(); // 外层
        _manager.EnableTransaction(); // 内层
        _dbContext.Users.Add(new TestUser { UserName = "inner-failed", Email = "if@t.test" });

        await _manager.RollbackTransactionAsync(); // 内层失败回滚：整个环境事务一起撤销

        Assert.Contains(_logs.Entries, e => e.Message.Contains("inner unit of work rolled back"));
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("nothing is committed by this call"));

        // 调用方吞掉内层异常后继续，最终外层「提交」
        await _manager.CommitTransactionAsync();

        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("nothing is committed by this call"));
        Assert.False(await PersistedAsync("inner-failed"));
    }

    [Fact]
    public async Task NestedRollback_ThenOuterAborts_DoesNotWarn()
    {
        _manager.EnableTransaction(); // 外层
        _manager.EnableTransaction(); // 内层
        _dbContext.Users.Add(new TestUser { UserName = "inner-aborted", Email = "ia@t.test" });

        await _manager.RollbackTransactionAsync(); // 内层失败
        await _manager.RollbackTransactionAsync(); // 外层按规定中止

        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Warning && e.Message.Contains("unit of work"));

        // 记账已消化：同一作用域里之后一次正常的提交不会被误报
        _manager.EnableTransaction();
        await _manager.CommitTransactionAsync();
        await _manager.CommitTransactionAsync(); // 多出来的一次提交（无事务）不归咎于先前的内层回滚

        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("nothing is committed by this call"));
    }

    [Fact]
    public async Task SequentialTransactions_DoNotWarn()
    {
        _manager.EnableTransaction();
        _dbContext.Users.Add(new TestUser { UserName = "seq-a", Email = "sa@t.test" });
        await _manager.CommitTransactionAsync();

        _manager.EnableTransaction();
        _dbContext.Users.Add(new TestUser { UserName = "seq-b", Email = "sb@t.test" });
        await _manager.RollbackTransactionAsync();

        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("inner unit of work"));
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("nothing is committed by this call"));
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
