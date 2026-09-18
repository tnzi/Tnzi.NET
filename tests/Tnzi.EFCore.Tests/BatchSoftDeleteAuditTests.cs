using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// <c>BatchSoftDeleteAsync</c> 必须产出与仓储 <c>DeleteAsync(predicate)</c> 逐列相同的软删行。
/// </summary>
/// <remarks>
/// <para>
/// 此前它只写 <c>IsDeleted</c>。<c>ExecuteUpdateAsync</c> 绕过变更跟踪器，2026-07-31 补在拦截器层的
/// 就地软删补写在这条路径上根本不执行 ⇒ 又一批「没有人、在没有时间删掉的」软删行，并发戳不变
/// （持旧快照的写入仍能通过）。同一语义在同一个仓库里两条路径必须相同。
/// </para>
/// <para>
/// 真实 SQLite；当前用户与时间来自应用容器（固定时间提供者），与仓储解析的是同一份。
/// </para>
/// </remarks>
public class BatchSoftDeleteAuditTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 9, 12, 8, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Deleter = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ServiceProvider _serviceProvider;
    private readonly TestDbContext _dbContext;
    private readonly MockCurrentUser _currentUser = new();
    private readonly SqliteConnection _connection;

    public BatchSoftDeleteAuditTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(_currentUser);
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestDbContext>();
        _dbContext.Database.EnsureCreated();
    }

    private async Task<TestAuditedDocument> SeedAsync(string title)
    {
        var document = new TestAuditedDocument { Title = title };
        _dbContext.AuditedDocuments.Add(document);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return document;
    }

    private Task<TestAuditedDocument> ReloadAsync(Guid id) =>
        _dbContext.AuditedDocuments.IgnoreQueryFilters().AsNoTracking().FirstAsync(d => d.Id == id);

    private static void AssertSoftDeletedBy(TestAuditedDocument row, Guid? deleterId, DateTime deletionTime, string originalStamp)
    {
        Assert.True(row.IsDeleted);
        Assert.Equal(deleterId, row.DeleterId);
        Assert.Equal(deletionTime, row.DeletionTime);
        Assert.Equal(deletionTime, row.LastModificationTime);
        Assert.Equal(deleterId, row.LastModifierId);
        Assert.NotEqual(originalStamp, row.ConcurrencyStamp);
        Assert.False(string.IsNullOrEmpty(row.ConcurrencyStamp));
    }

    /// <summary>查询根（DbSet）暴露 DbContext，删除人与时间自动解析。</summary>
    [Fact]
    public async Task BatchSoftDeleteAsync_QueryRoot_WritesDeleterAndDeletionTimeAndBumpsConcurrencyStamp()
    {
        var archived = await SeedAsync("archive-1");
        _currentUser.SetUser(Deleter, "deleter");

        var affected = await ((IQueryable<TestAuditedDocument>)_dbContext.AuditedDocuments).BatchSoftDeleteAsync();

        Assert.Equal(1, affected);
        AssertSoftDeletedBy(await ReloadAsync(archived.Id), Deleter, FixedNow, archived.ConcurrencyStamp);
    }

    /// <summary>
    /// 失败关闭：Where 之后的组合查询在 EF Core 里没有公开途径拿到 DbContext，实体又记录删除人 ⇒ 拒绝并指向
    /// 显式重载，而不是安静地少写几列。
    /// </summary>
    [Fact]
    public async Task BatchSoftDeleteAsync_ComposedQuery_RequiresExplicitActor()
    {
        var archived = await SeedAsync("archive-composed");
        _currentUser.SetUser(Deleter, "deleter");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _dbContext.AuditedDocuments.Where(d => d.Title == "archive-composed").BatchSoftDeleteAsync());

        Assert.Contains("BatchSoftDeleteByAsync", ex.Message);
        Assert.False((await ReloadAsync(archived.Id)).IsDeleted);
    }

    [Fact]
    public async Task BatchSoftDeleteAsync_DbSetPredicate_LeavesNonMatchingRowsUntouched()
    {
        var archived = await SeedAsync("archive-1b");
        var kept = await SeedAsync("keep-1b");
        _currentUser.SetUser(Deleter, "deleter");

        var affected = await _dbContext.AuditedDocuments.BatchSoftDeleteAsync(d => d.Title.StartsWith("archive"));

        Assert.Equal(1, affected);
        AssertSoftDeletedBy(await ReloadAsync(archived.Id), Deleter, FixedNow, archived.ConcurrencyStamp);

        var untouched = await ReloadAsync(kept.Id);
        Assert.False(untouched.IsDeleted);
        Assert.Null(untouched.DeleterId);
        Assert.Equal(kept.ConcurrencyStamp, untouched.ConcurrencyStamp);
    }

    [Fact]
    public async Task BatchSoftDeleteAsync_DbSet_WritesDeleterAndDeletionTimeAndBumpsConcurrencyStamp()
    {
        var archived = await SeedAsync("archive-2");
        _currentUser.SetUser(Deleter, "deleter");

        var affected = await _dbContext.AuditedDocuments.BatchSoftDeleteAsync(d => d.Title == "archive-2");

        Assert.Equal(1, affected);
        AssertSoftDeletedBy(await ReloadAsync(archived.Id), Deleter, FixedNow, archived.ConcurrencyStamp);
    }

    /// <summary>同一语义两条路径逐列相同：仓储 DeleteAsync(predicate) 与 BatchSoftDeleteAsync。</summary>
    [Fact]
    public async Task BatchSoftDeleteAsync_ProducesSameColumnsAsRepositoryDeleteAsync()
    {
        var viaRepository = await SeedAsync("parity-repo");
        var viaExtension = await SeedAsync("parity-ext");
        _currentUser.SetUser(Deleter, "deleter");

        var repository = new EFCoreRepository<TestDbContext, TestAuditedDocument, Guid>(
            _dbContext, options: null, serviceProvider: _serviceProvider, logger: null);
        await repository.DeleteAsync(d => d.Title == "parity-repo");
        await _dbContext.AuditedDocuments.BatchSoftDeleteAsync(d => d.Title == "parity-ext");

        var expected = await ReloadAsync(viaRepository.Id);
        var actual = await ReloadAsync(viaExtension.Id);

        Assert.Equal(expected.IsDeleted, actual.IsDeleted);
        Assert.Equal(expected.DeleterId, actual.DeleterId);
        Assert.Equal(expected.DeletionTime, actual.DeletionTime);
        Assert.Equal(expected.LastModifierId, actual.LastModifierId);
        Assert.Equal(expected.LastModificationTime, actual.LastModificationTime);
        Assert.NotEqual(viaRepository.ConcurrencyStamp, expected.ConcurrencyStamp);
        Assert.NotEqual(viaExtension.ConcurrencyStamp, actual.ConcurrencyStamp);
    }

    /// <summary>
    /// 仓储只有 <c>AsQueryable(withTracking: true)</c> 才是查询根（<c>Queryable.AsQueryable</c> 对已是
    /// <c>IQueryable</c> 的 DbSet 原样返回）；缺省的 <c>AsQueryable()</c> 已经组合了 <c>AsNoTracking()</c>，
    /// 是一条组合查询，拿不到 DbContext。文档曾把「仓储 AsQueryable()」笼统地列为查询根。
    /// </summary>
    [Fact]
    public async Task BatchSoftDeleteAsync_RepositoryTrackingQueryable_IsAQueryRoot()
    {
        var archived = await SeedAsync("archive-repo-root");
        _currentUser.SetUser(Deleter, "deleter");
        var repository = new EFCoreRepository<TestDbContext, TestAuditedDocument, Guid>(
            _dbContext, options: null, serviceProvider: _serviceProvider, logger: null);

        var affected = await repository.AsQueryable(withTracking: true).BatchSoftDeleteAsync();

        Assert.Equal(1, affected);
        AssertSoftDeletedBy(await ReloadAsync(archived.Id), Deleter, FixedNow, archived.ConcurrencyStamp);
    }

    [Fact]
    public async Task BatchSoftDeleteAsync_RepositoryDefaultQueryable_IsComposedAndRequiresExplicitActor()
    {
        var archived = await SeedAsync("archive-repo-notracking");
        _currentUser.SetUser(Deleter, "deleter");
        var repository = new EFCoreRepository<TestDbContext, TestAuditedDocument, Guid>(
            _dbContext, options: null, serviceProvider: _serviceProvider, logger: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AsQueryable().BatchSoftDeleteAsync());

        Assert.Contains("BatchSoftDeleteByAsync", ex.Message);
        Assert.False((await ReloadAsync(archived.Id)).IsDeleted);
    }

    /// <summary>显式重载：调用方自带删除人与时间（后台作业、迁移脚本）。</summary>
    [Fact]
    public async Task BatchSoftDeleteAsync_ExplicitDeleterAndTime_WritesThem()
    {
        var archived = await SeedAsync("archive-explicit");
        var explicitDeleter = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var explicitTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await _dbContext.AuditedDocuments
            .Where(d => d.Title == "archive-explicit")
            .BatchSoftDeleteByAsync(explicitDeleter, explicitTime);

        AssertSoftDeletedBy(await ReloadAsync(archived.Id), explicitDeleter, explicitTime, archived.ConcurrencyStamp);
    }

    /// <summary>纯 ISoftDelete 实体（没有审计字段）不需要操作人：组合查询也照常只写 IsDeleted，行为不变。</summary>
    [Fact]
    public async Task BatchSoftDeleteAsync_PlainSoftDeleteEntity_OnlySetsIsDeleted()
    {
        _dbContext.SoftDeletableProducts.Add(new TestSoftDeletableProduct { Name = "plain", Price = 1m });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var affected = await _dbContext.SoftDeletableProducts.Where(p => p.Name == "plain").BatchSoftDeleteAsync();

        Assert.Equal(1, affected);
        Assert.True((await _dbContext.SoftDeletableProducts.IgnoreQueryFilters().FirstAsync(p => p.Name == "plain")).IsDeleted);
    }

    /// <summary>
    /// 失败关闭：拿不到当前用户（手工构造的 DbContext 没有应用容器）时拒绝，指向显式重载，
    /// 而不是安静地少写几列。
    /// </summary>
    [Fact]
    public async Task BatchSoftDeleteAsync_NoApplicationContainer_ThrowsAndChangesNothing()
    {
        var archived = await SeedAsync("archive-orphan");

        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options;
        using var orphanContext = new TestDbContext(options, new MockCurrentUser());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orphanContext.AuditedDocuments.BatchSoftDeleteAsync(d => d.Title == "archive-orphan"));

        Assert.Contains("BatchSoftDeleteByAsync", ex.Message);
        Assert.False((await ReloadAsync(archived.Id)).IsDeleted);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
