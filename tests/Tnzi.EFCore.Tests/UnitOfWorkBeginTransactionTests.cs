namespace Tnzi.EFCore.Tests;

/// <summary>
/// <see cref="IUnitOfWork.BeginTransactionAsync"/> 与 <see cref="IUnitOfWork.EnableTransaction"/> 等价：深度 +1，可嵌套，
/// 与 <see cref="IUnitOfWork.CommitTransactionAsync"/> 成对使用，只在最外层真正提交。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前 <c>EFCoreUnitOfWork.BeginTransactionAsync</c> 只开物理事务、从不计深度：仓储因为看到活动事务而缓冲写入，
/// 配对的 <c>CommitTransactionAsync</c> 却因深度为 0 抛「Transaction is not enabled」并回滚，
/// 文档里「Begin → 写入 → Commit」的示例每一次都写不进库；嵌套的第二次 Begin 抛「Transaction already started」。
/// </para>
/// <para>
/// 夹具按应用的接线装配：同一作用域里仓储与工作单元共享上下文，仓储经容器里的 <see cref="IUnitOfWork"/> 判断是否在事务中。
/// </para>
/// </remarks>
public sealed class UnitOfWorkBeginTransactionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _provider;

    public UnitOfWorkBeginTransactionTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<IUnitOfWork>(sp => new EFCoreUnitOfWork<TestDbContext>(sp.GetRequiredService<TestDbContext>(), null, sp));
        services.AddScoped<IRepository<TestProduct, Guid>>(sp =>
            new EFCoreRepository<TestDbContext, TestProduct, Guid>(sp.GetRequiredService<TestDbContext>(), null, sp));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task BeginWriteCommit_PersistsTheWrite()
    {
        var id = Guid.NewGuid();
        using (var scope = _provider.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestProduct, Guid>>();

            await unitOfWork.BeginTransactionAsync();
            await repository.InsertAsync(new TestProduct { Id = id, Name = "order", Price = 1m, Stock = 1 });
            await unitOfWork.CommitTransactionAsync();

            Assert.Equal(0, unitOfWork.TransactionDepth);
            Assert.False(unitOfWork.IsEnabledTransaction);
        }

        Assert.True(await ExistsAsync(id));
    }

    [Fact]
    public async Task BeginWriteRollback_DiscardsTheWrite()
    {
        var id = Guid.NewGuid();
        using (var scope = _provider.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestProduct, Guid>>();

            await unitOfWork.BeginTransactionAsync();
            await repository.InsertAsync(new TestProduct { Id = id, Name = "order", Price = 1m, Stock = 1 });
            await unitOfWork.RollbackTransactionAsync();
        }

        Assert.False(await ExistsAsync(id));
    }

    [Fact]
    public async Task NestedBegin_CommitsOnlyAtTheOutermostLevel()
    {
        var outerId = Guid.NewGuid();
        var innerId = Guid.NewGuid();
        using (var scope = _provider.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestProduct, Guid>>();

            await unitOfWork.BeginTransactionAsync();
            await repository.InsertAsync(new TestProduct { Id = outerId, Name = "outer", Price = 1m, Stock = 1 });

            await unitOfWork.BeginTransactionAsync();
            Assert.Equal(2, unitOfWork.TransactionDepth);
            await repository.InsertAsync(new TestProduct { Id = innerId, Name = "inner", Price = 1m, Stock = 1 });
            await unitOfWork.CommitTransactionAsync();

            // 内层提交只退一层：外层仍在事务中，写入仍缓冲在变更跟踪器里
            Assert.Equal(1, unitOfWork.TransactionDepth);
            Assert.True(unitOfWork.IsEnabledTransaction);
            Assert.False(await ExistsAsync(innerId));

            await unitOfWork.CommitTransactionAsync();
            Assert.Equal(0, unitOfWork.TransactionDepth);
        }

        Assert.True(await ExistsAsync(outerId));
        Assert.True(await ExistsAsync(innerId));
    }

    /// <summary>保存时延迟开启物理事务不改变深度（拆分公开 Begin 与内部 BEGIN 的回归守卫）。</summary>
    [Fact]
    public async Task LazyPhysicalBegin_OnSave_LeavesTheDepthAlone()
    {
        var id = Guid.NewGuid();
        using (var scope = _provider.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

            unitOfWork.EnableTransaction();
            context.Products.Add(new TestProduct { Id = id, Name = "flushed", Price = 1m, Stock = 1 });
            await unitOfWork.SaveChangesAsync();

            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Equal(1, unitOfWork.TransactionDepth);

            await unitOfWork.CommitTransactionAsync();
            Assert.Null(context.Database.CurrentTransaction);
        }

        Assert.True(await ExistsAsync(id));
    }

    /// <summary>从一个全新作用域（新上下文、同一连接）读，只看得到已提交或已 flush 的行。</summary>
    private async Task<bool> ExistsAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Products.AsNoTracking().AnyAsync(p => p.Id == id);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
