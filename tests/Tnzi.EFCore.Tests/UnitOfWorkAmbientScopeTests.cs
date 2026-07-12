namespace Tnzi.EFCore.Tests;

/// <summary>
/// UnitOfWorkManager 环境事务作用域(AmbientUnitOfWork)生命周期测试。
/// 事务启用时把自身发布为 IAmbientUnitOfWorkScope(AsyncLocal),供事件总线等基础设施
/// 感知活跃事务并把副作用延迟到提交后;提交/回滚/释放时清除。
/// </summary>
public class UnitOfWorkAmbientScopeTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly TestDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly IPostCommitActionQueue _postCommitQueue;
    private readonly SqliteConnection _connection;

    public UnitOfWorkAmbientScopeTests()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite(_connection);
        });

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(TestDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestDbContext>();
        _manager = _serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        _postCommitQueue = _serviceProvider.GetRequiredService<IPostCommitActionQueue>();

        _dbContext.Database.EnsureCreated();
        AmbientUnitOfWork.Set(null);
    }

    [Fact]
    public async Task EnableTransaction_PublishesAmbientScope_AndCommitDeactivatesIt()
    {
        Assert.Null(AmbientUnitOfWork.Current);

        _manager.EnableTransaction();

        var ambient = AmbientUnitOfWork.Current;
        Assert.NotNull(ambient);
        Assert.True(ambient.IsTransactionActive);
        Assert.Same(_manager, ambient);

        await _manager.CommitTransactionAsync();

        // AsyncLocal 边界:CommitTransactionAsync 内部的 Set(null) 不会传播回调用者流,
        // 调用者流可能残留引用,但 IsTransactionActive 必须为 false(消费方据此判定,语义等价于清除)
        var after = AmbientUnitOfWork.Current;
        Assert.True(after == null || !after.IsTransactionActive,
            "Ambient scope must be inactive after commit");
    }

    [Fact]
    public async Task EnqueuePostCommit_ActionRunsAfterCommit_NotBefore()
    {
        _manager.EnableTransaction();
        var executed = false;

        var ambient = Assert.IsAssignableFrom<IAmbientUnitOfWorkScope>(AmbientUnitOfWork.Current);
        ambient.EnqueuePostCommit(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        Assert.False(executed);
        Assert.Equal(1, _postCommitQueue.Count);

        await _manager.CommitTransactionAsync();

        Assert.True(executed);
        Assert.Equal(0, _postCommitQueue.Count);
    }

    [Fact]
    public async Task Rollback_ClearsAmbientScope_AndDiscardsPostCommitActions()
    {
        _manager.EnableTransaction();
        var executed = false;

        var ambient = Assert.IsAssignableFrom<IAmbientUnitOfWorkScope>(AmbientUnitOfWork.Current);
        ambient.EnqueuePostCommit(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        await _manager.RollbackTransactionAsync();

        // 同 Commit:调用者流可能残留 inactive 引用,行为语义以 IsTransactionActive 为准
        var after = AmbientUnitOfWork.Current;
        Assert.True(after == null || !after.IsTransactionActive,
            "Ambient scope must be inactive after rollback");
        Assert.False(executed);
        Assert.Equal(0, _postCommitQueue.Count);
    }

    [Fact]
    public async Task NestedTransaction_AmbientScopeSurvivesInnerCommit_ClearedByOuterCommit()
    {
        _manager.EnableTransaction(); // 外层
        _manager.EnableTransaction(); // 内层

        Assert.NotNull(AmbientUnitOfWork.Current);

        // 内层提交(嵌套 flush):环境作用域仍然活跃,提交权在最外层
        await _manager.CommitTransactionAsync();
        var midway = AmbientUnitOfWork.Current;
        Assert.NotNull(midway);
        Assert.True(midway.IsTransactionActive);
        Assert.True(_manager.IsEnabledTransaction);

        // 最外层提交:作用域失活(调用者流可能残留 inactive 引用,见 AsyncLocal 边界)
        await _manager.CommitTransactionAsync();
        var after = AmbientUnitOfWork.Current;
        Assert.True(after == null || !after.IsTransactionActive);
        Assert.False(_manager.IsEnabledTransaction);
    }

    public void Dispose()
    {
        AmbientUnitOfWork.Set(null);
        _serviceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
