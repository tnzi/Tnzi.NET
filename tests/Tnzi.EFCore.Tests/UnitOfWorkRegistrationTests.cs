namespace Tnzi.EFCore.Tests;

/// <summary>
/// 非泛型 <see cref="IUnitOfWork"/> 的注册归属测试。
///
/// 回归背景：<c>AddTnziDbContext</c> 早先对每个 DbContext 都无条件注册
/// <c>IUnitOfWork -> EFCoreUnitOfWork&lt;TDbContext&gt;</c>，多 DbContext 应用里
/// 最后注册的那个胜出，注入 IUnitOfWork 的服务于是把变更保存到了**非主**上下文：
/// 主上下文里 Added 的实体既不 INSERT 也不报错，紧接着按 Id 回查就查不到
/// （现网表现是写完立刻读的接口返回 404），变更最后随作用域释放被静默丢弃。
/// </summary>
public class UnitOfWorkRegistrationTests
{
    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new Mock<ICurrentUser>().Object);
        return services;
    }

    [Fact]
    public void IUnitOfWork_ResolvesToPrimaryDbContext_WhenPrimaryRegisteredFirst()
    {
        var services = CreateServices();
        services.AddTnziDbContext<TestDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);
        services.AddTnziDbContext<DefaultDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: false);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.IsType<EFCoreUnitOfWork<TestDbContext>>(unitOfWork);
    }

    [Fact]
    public void IUnitOfWork_ResolvesToPrimaryDbContext_WhenPrimaryRegisteredLast()
    {
        var services = CreateServices();
        services.AddTnziDbContext<DefaultDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: false);
        services.AddTnziDbContext<TestDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.IsType<EFCoreUnitOfWork<TestDbContext>>(unitOfWork);
    }

    /// <summary>
    /// 单 DbContext 且未标 isPrimary 是既有用法（手工调用 AddTnziDbContext 时该参数默认 false），
    /// 必须继续能解析出 IUnitOfWork，否则所有注入它的服务在启动/解析期就炸。
    /// </summary>
    [Fact]
    public void IUnitOfWork_StillResolves_WhenSingleContextNotMarkedPrimary()
    {
        var services = CreateServices();
        services.AddTnziDbContext<TestDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.IsType<EFCoreUnitOfWork<TestDbContext>>(unitOfWork);
    }

    /// <summary>
    /// 多个非主上下文时取第一个（确定性），而不是"最后注册的赢"。
    /// </summary>
    [Fact]
    public void IUnitOfWork_IsDeterministic_WhenNoContextMarkedPrimary()
    {
        var services = CreateServices();
        services.AddTnziDbContext<TestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddTnziDbContext<DefaultDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.IsType<EFCoreUnitOfWork<TestDbContext>>(unitOfWork);
    }

    /// <summary>
    /// 真实写入路径：主上下文里插入的实体，经注入的 IUnitOfWork 保存后必须真的落库
    /// （回归前这里保存的是另一个上下文，SaveChanges 返回 0，随后回查为 null）。
    /// </summary>
    [Fact]
    public async Task InjectedUnitOfWork_PersistsChangesOfPrimaryDbContext()
    {
        using var primaryConnection = new SqliteConnection("DataSource=:memory:");
        using var secondaryConnection = new SqliteConnection("DataSource=:memory:");
        await primaryConnection.OpenAsync();
        await secondaryConnection.OpenAsync();

        var services = CreateServices();
        // 非主上下文先注册，模拟"后注册者夺走 IUnitOfWork"的原始场景的反面
        services.AddTnziDbContext<DefaultDbContext>(o => o.UseSqlite(secondaryConnection), isPrimary: false);
        services.AddTnziDbContext<TestDbContext>(o => o.UseSqlite(primaryConnection), isPrimary: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var product = new TestProduct { Name = "Widget", Price = 9.99m };
        dbContext.Set<TestProduct>().Add(product);

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var affected = await unitOfWork.SaveChangesAsync();

        Assert.True(affected > 0);
        Assert.NotEqual(default, product.Id);

        var reloaded = await dbContext.Set<TestProduct>().FirstOrDefaultAsync(p => p.Id == product.Id);
        Assert.NotNull(reloaded);
    }
}
