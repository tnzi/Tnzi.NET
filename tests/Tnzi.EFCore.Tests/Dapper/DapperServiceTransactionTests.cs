using Tnzi.EFCore.Dapper;
using Tnzi.EFCore.Dapper.Providers;

namespace Tnzi.EFCore.Tests.Dapper;

/// <summary>
/// <see cref="DapperService"/> 写操作必须加入工作单元的延迟事务。
/// </summary>
/// <remarks>
/// <para>
/// 框架物理事务延迟到首次 UoW SaveChanges 才 BEGIN；此前 <c>DapperService</c> 只读
/// <c>Database.CurrentTransaction</c>，在「事务内第一次 flush 之前」它恒为 null，裸 SQL 以自动提交落库、
/// 回滚撤不掉。仓储的同形路径 2026-07-07 已补 <c>EnsureBulkSqlJoinsTransactionAsync</c>，Dapper 未跟上。
/// </para>
/// <para>
/// ★ 用例走「目标行已存在」的稳态路径且中途不 flush：回滚用例若走「新建 + flush」，flush 会顺手 BEGIN
/// 延迟事务而掩盖缺失（2026-08-23 教训）。真实 SQLite + <see cref="UnitOfWorkManager"/>，不 Mock 被测对象。
/// </para>
/// </remarks>
public class DapperServiceTransactionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly TestDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly SqliteConnection _connection;

    public DapperServiceTransactionTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(TestDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestDbContext>();
        _manager = _serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        _dbContext.Database.EnsureCreated();
    }

    private DapperService CreateService() => new(_dbContext, new SqliteTestProvider(), _manager);

    private async Task<Guid> SeedProductAsync(string name, int stock)
    {
        var product = new TestProduct { Name = name, Price = 1m, Stock = stock };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return product.Id;
    }

    [Fact]
    public async Task ExecuteAsync_AsFirstWriteInsideUnitOfWork_IsRolledBack()
    {
        var id = await SeedProductAsync("keep-stock", 7);
        var dapper = CreateService();

        _manager.EnableTransaction();
        await dapper.ExecuteAsync("UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name", new { Name = "keep-stock" });

        Assert.NotNull(_dbContext.Database.CurrentTransaction);

        await _manager.RollbackTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.Equal(7, reloaded!.Stock);
    }

    [Fact]
    public async Task ExecuteScalarAsync_AsFirstWriteInsideUnitOfWork_IsRolledBack()
    {
        var id = await SeedProductAsync("scalar-stock", 3);
        var dapper = CreateService();

        _manager.EnableTransaction();
        var affected = await dapper.ExecuteScalarAsync<long>(
            "UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name; SELECT changes()", new { Name = "scalar-stock" });
        Assert.Equal(1, affected);

        await _manager.RollbackTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.Equal(3, reloaded!.Stock);
    }

    [Fact]
    public async Task BulkDeleteAsync_AsFirstWriteInsideUnitOfWork_IsRolledBack()
    {
        var id = await SeedProductAsync("bulk-delete-keep", 1);
        var dapper = CreateService();

        _manager.EnableTransaction();
        var deleted = await dapper.BulkDeleteAsync<TestProduct>([id]);
        Assert.Equal(1, deleted);

        await _manager.RollbackTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        Assert.NotNull(await _dbContext.Products.FindAsync(id));
    }

    [Fact]
    public async Task ExecuteAsync_InsideUnitOfWork_CommitPersists()
    {
        var id = await SeedProductAsync("commit-stock", 5);
        var dapper = CreateService();

        _manager.EnableTransaction();
        await dapper.ExecuteAsync("UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name", new { Name = "commit-stock" });
        await _manager.CommitTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.Equal(0, reloaded!.Stock);
    }

    /// <summary>没有工作单元事务时保持自动提交：这是正确语义，不是缺陷。</summary>
    [Fact]
    public async Task ExecuteAsync_WithoutUnitOfWorkTransaction_AutoCommits()
    {
        var id = await SeedProductAsync("autocommit-stock", 9);
        var dapper = CreateService();

        await dapper.ExecuteAsync("UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name", new { Name = "autocommit-stock" });

        Assert.Null(_dbContext.Database.CurrentTransaction);
        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.Equal(0, reloaded!.Stock);
    }

    /// <summary>
    /// 失败关闭：事务已启用、但这个 DbContext 实例不是工作单元管理的那个（手工 new 出来的），
    /// 加入事务不可能成功 —— 必须抛出，不能静默回落自动提交。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TransactionEnabledButContextNotManaged_Throws()
    {
        await SeedProductAsync("foreign-context", 2);

        var foreignOptions = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options;
        using var foreignContext = new TestDbContext(foreignOptions, new MockCurrentUser());
        var dapper = new DapperService(foreignContext, new SqliteTestProvider(), _manager);

        _manager.EnableTransaction();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dapper.ExecuteAsync("UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name", new { Name = "foreign-context" }));

        Assert.Contains(nameof(TestDbContext), ex.Message);
        await _manager.RollbackTransactionAsync();
    }

    /// <summary>
    /// 仓储守卫的第二个分支：没有管理器、只有直接注入的作用域 <see cref="IUnitOfWork"/> 开了延迟事务时，
    /// Dapper 写操作同样要先加入它（此前只认管理器，这条路上的写入照旧自动提交）。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ScopedUnitOfWorkTransactionWithoutManager_IsRolledBack()
    {
        var id = await SeedProductAsync("scoped-uow-stock", 4);
        var unitOfWork = new EFCoreUnitOfWork<TestDbContext>(_dbContext);
        var dapper = new DapperService(_dbContext, new SqliteTestProvider(), unitOfWorkManager: null, unitOfWork);

        unitOfWork.EnableTransaction();
        await dapper.ExecuteAsync("UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name", new { Name = "scoped-uow-stock" });

        Assert.NotNull(_dbContext.Database.CurrentTransaction);

        await unitOfWork.RollbackTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.Equal(4, reloaded!.Stock);
    }

    /// <summary>管理器在但没开事务、作用域工作单元开了：仍按后者加入（管理器分支不吞掉第二个分支）。</summary>
    [Fact]
    public async Task ExecuteAsync_ManagerIdleButScopedUnitOfWorkEnabled_JoinsScopedTransaction()
    {
        var id = await SeedProductAsync("manager-idle-stock", 6);
        var unitOfWork = new EFCoreUnitOfWork<TestDbContext>(_dbContext);
        var dapper = new DapperService(_dbContext, new SqliteTestProvider(), _manager, unitOfWork);

        unitOfWork.EnableTransaction();
        await dapper.ExecuteAsync("UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name", new { Name = "manager-idle-stock" });

        Assert.NotNull(_dbContext.Database.CurrentTransaction);

        await unitOfWork.RollbackTransactionAsync();

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Products.FindAsync(id);
        Assert.Equal(6, reloaded!.Stock);
    }

    /// <summary>
    /// 失败关闭同样适用于第二个分支：作用域工作单元绑的是另一个上下文实例（多 DbContext 应用里非主上下文的 Dapper 写入），
    /// 加入不了就抛，不静默自动提交。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ScopedUnitOfWorkBoundToAnotherContext_Throws()
    {
        await SeedProductAsync("foreign-uow", 2);
        var foreignOptions = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options;
        using var foreignContext = new TestDbContext(foreignOptions, new MockCurrentUser());
        var unitOfWork = new EFCoreUnitOfWork<TestDbContext>(foreignContext);
        var dapper = new DapperService(_dbContext, new SqliteTestProvider(), unitOfWorkManager: null, unitOfWork);

        unitOfWork.EnableTransaction();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dapper.ExecuteAsync("UPDATE \"TestProducts\" SET \"Stock\" = 0 WHERE \"Name\" = @Name", new { Name = "foreign-uow" }));

        Assert.Contains(nameof(TestDbContext), ex.Message);
        await unitOfWork.RollbackTransactionAsync();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 测试用 SQLite 方言：框架的 Dapper provider 工厂对 SQLite 直接抛 NotSupported，
/// 而批量 SQL 只需要标识符转义与方言名，双引号转义在 SQLite 上成立。
/// </summary>
internal sealed class SqliteTestProvider : IDatabaseProvider
{
    public string DatabaseType => "Sqlite";

    /// <summary>SQLITE_MAX_VARIABLE_NUMBER（3.32+ 默认 32766）；测试可调小以逼出分批。</summary>
    public int MaxParametersPerCommand { get; init; } = 32766;

    public string EscapeIdentifier(string identifier) => $"\"{identifier}\"";

    public string ApplyPaging(string sql, int offset, int limit) => $"{sql} LIMIT {limit} OFFSET {offset}";

    public string ApplyReturningId(string sql, string keyColumn) => $"{sql}; SELECT last_insert_rowid()";

    public System.Data.IDbConnection CreateConnection(string connectionString) => new SqliteConnection(connectionString);
}
