namespace Tnzi.EFCore.Tests;

using Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// DeleteAsync 快路径（ExecuteUpdate 软删除 / ExecuteDelete 硬删除）事务护栏回归测试。
/// 框架事务延迟物理开启（首次 UoW SaveChanges 才 BEGIN）；当删除是请求内首个写操作时，
/// 裸 SQL 曾在自动提交模式落库、UoW 回滚撤不掉。修复后删除前会幂等地强开物理事务，回滚可撤销。
/// 真实 SQLite + UnitOfWorkManager 全局事务。
/// </summary>
public class DeleteAsyncTransactionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly TestDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly SqliteConnection _connection;

    public DeleteAsyncTransactionTests()
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

    private EFCoreRepository<TestDbContext, TEntity, Guid> CreateRepository<TEntity>()
        where TEntity : class, IEntity<Guid>
        => new(_dbContext, options: null, serviceProvider: _serviceProvider, logger: null);

    [Fact]
    public async Task DeleteAsync_HardDelete_AsFirstWriteInGlobalTransaction_RollbackKeepsRow()
    {
        // Arrange：先提交一条基线记录（此时未启用事务，立即落库）
        var product = new TestProduct { Name = "keep-me", Price = 9.9m, Stock = 3 };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();
        var id = product.Id;
        _dbContext.ChangeTracker.Clear();

        var repository = CreateRepository<TestProduct>();

        // Act：启用全局事务；删除作为事务内“首个写操作”；随后回滚
        _manager.EnableTransaction();
        await repository.DeleteAsync(id);

        // 修复后：ExecuteDelete 前已强开物理事务
        Assert.NotNull(_dbContext.Database.CurrentTransaction);

        await _manager.RollbackTransactionAsync();

        // Assert：回滚撤销了 ExecuteDelete，行仍存在
        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_AsFirstWriteInGlobalTransaction_RollbackKeepsRowNotDeleted()
    {
        // Arrange：基线软删除记录（IsDeleted = false）
        var product = new TestSoftDeletableProduct { Name = "soft-keep", Price = 5m, Stock = 2 };
        _dbContext.SoftDeletableProducts.Add(product);
        await _dbContext.SaveChangesAsync();
        var id = product.Id;
        _dbContext.ChangeTracker.Clear();

        var repository = CreateRepository<TestSoftDeletableProduct>();

        // Act：启用全局事务；软删除作为首个写操作（走 ExecuteUpdate）；随后回滚
        _manager.EnableTransaction();
        await repository.DeleteAsync(id);

        Assert.NotNull(_dbContext.Database.CurrentTransaction);

        await _manager.RollbackTransactionAsync();

        // Assert：回滚撤销了 ExecuteUpdate，行仍存在且 IsDeleted 仍为 false
        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.SoftDeletableProducts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsDeleted);
    }

    [Fact]
    public async Task ManagerSaveChanges_AsFirstWriteInTransaction_StartsPhysicalTx_GeneratesId_AndRollbackUndoes()
    {
        // Backs ApplicationService.FlushAsync (write → flush → read within a UoW) and
        // the CrudAppService.CreateAsync flush-before-map. The fix routes the manager
        // flush through the UoW so the deferred physical transaction is started (rather
        // than autocommitting the first save) and the generated Id is populated.
        var repository = CreateRepository<TestProduct>();

        _manager.EnableTransaction();
        var product = new TestProduct { Name = "flush-me", Price = 1m, Stock = 1 };
        await repository.InsertAsync(product); // only TRACKS under an enabled transaction (no save yet)

        await _manager.SaveChangesAsync(); // the path behind FlushAsync

        // Transaction-safety: the flush started the physical transaction (not autocommit).
        Assert.NotNull(_dbContext.Database.CurrentTransaction);
        // The flush ran SaveChanges → the sequential-GUID Id is now generated (the
        // mechanism CrudAppService relies on to return a non-empty DTO).
        Assert.NotEqual(Guid.Empty, product.Id);
        var id = product.Id;

        await _manager.RollbackTransactionAsync();

        // Rollback undoes the flushed insert → it was inside the transaction, not autocommitted.
        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.Null(reloaded);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _connection?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
