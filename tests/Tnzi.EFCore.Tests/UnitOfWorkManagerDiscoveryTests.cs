namespace Tnzi.EFCore.Tests;

/// <summary>
/// <see cref="UnitOfWorkManager"/> 对主 DbContext 的发现：它决定提交循环里有没有东西可提交。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是一条静默丢写：<c>Database:DbContexts</c> 只写 <c>Name</c> 不写 <c>DbContextType</c>
/// 是文档明确支持的形态（启动期按名字自动发现），但 <c>UnitOfWorkManager</c> 此前只有两条发现途径 ——
/// 重新绑定配置后读 <c>DbContextType</c>（空 ⇒ null），以及 <c>IEntityManager.GetAllDbContextTypes()</c>
/// （刻意排除承载「主上下文实体」的 <c>object</c> 占位键）。两条都取不到主上下文，于是
/// <c>EnableTransaction → 仓储写入 → CommitTransactionAsync</c> 一个 UoW 都不建、零次 SaveChanges、
/// 接口 200，写入随作用域释放消失。
/// </para>
/// <para>
/// 修法是让 <c>AddTnziDbContext</c> 这个所有 DbContext 都经过的注册漏斗登记类型，管理器优先读它。
/// 这组测试刻意<b>不 Mock</b> <see cref="IEntityManager"/>：既有的 UoW 测试全部 Mock 它返回主上下文，
/// 真实发现路径此前零覆盖。
/// </para>
/// </remarks>
public class UnitOfWorkManagerDiscoveryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>
    /// 与消费方 appsettings 的「只写 Name」形态一致的配置；没有 DbContextType。
    /// </summary>
    private static IConfiguration NameOnlyDatabaseConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:DbContexts:0:Name"] = "Test",
            ["Database:DbContexts:0:ConnectionString"] = "Data Source=:memory:",
            ["Database:DbContexts:0:Provider"] = "Sqlite",
        })
        .Build();

    private ServiceProvider BuildProvider(Action<IServiceCollection>? registerDbContext = null, bool ensureCreated = true)
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());
        services.AddSingleton(NameOnlyDatabaseConfiguration());

        // 真实的 EntityManager：TestDbContext 的实体没有 IEntityRegister，全部落在 object 占位键下，
        // 正是「主上下文实体 DbContextType 为 null」这个最常见的形态。
        services.AddSingleton<IEntityManager, EntityManager>();
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();

        if (registerDbContext != null)
        {
            registerDbContext(services);
        }
        else
        {
            services.AddTnziDbContext<TestDbContext>(options =>
            {
                options.UseSqlite(_connection);
                options.EnableSensitiveDataLogging();
            }, isPrimary: true);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        if (ensureCreated)
        {
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreated();
        }
        return provider;
    }

    /// <summary>
    /// 存在性证明：只写 Name 的配置下，事务内的仓储写入在提交后**真的落库**。
    /// 修复前：提交循环零次，作用域释放后按 Id 回查为空。
    /// </summary>
    [Fact]
    public async Task NameOnlyConfiguration_CommitsPrimaryContextWrites()
    {
        var provider = BuildProvider();
        Guid id;

        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var repository = new EFCoreRepository<TestDbContext, TestProduct, Guid>(
                dbContext, options: null, serviceProvider: scope.ServiceProvider, logger: null);

            // ExecuteInUnitOfWorkAsync / 全局 UoW 的形状：EnableTransaction → 写 → Commit，中途不 flush。
            manager.EnableTransaction();
            var product = new TestProduct { Name = "must-persist", Price = 1m };
            await repository.InsertAsync(product);
            id = product.Id;

            await manager.CommitTransactionAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var reloaded = await dbContext.Products.FindAsync(id);

            Assert.NotNull(reloaded);
        }
    }

    /// <summary>
    /// 同一条发现路径也支撑 <c>IUnitOfWorkManager.SaveChangesAsync</c>（<c>FlushAsync</c> 背后）：
    /// 发现不到主上下文时它返回 0 且什么都不保存。
    /// </summary>
    [Fact]
    public async Task NameOnlyConfiguration_SaveChangesReachesThePrimaryContext()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        dbContext.Products.Add(new TestProduct { Name = "flush-me", Price = 2m });
        var saved = await manager.SaveChangesAsync();

        Assert.Equal(1, saved);
    }

    /// <summary>
    /// 失败方向关闭：DbContext 绕过 <c>AddTnziDbContext</c>（裸 <c>AddDbContext</c>）且配置只写 Name 时，
    /// 管理器一个上下文都发现不了，而容器里明明有一个 DbContext —— 提交必须抛而不是「成功」。
    /// 只记 Warning 时写丢的形状与修复前一模一样：接口 200、事务内的写入随作用域消失，差别只是多了一行日志。
    /// </summary>
    [Fact]
    public async Task ContextRegisteredOutsideTheFunnel_FailsClosedOnCommit()
    {
        var provider = BuildProvider(services =>
            services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection)));

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        manager.EnableTransaction();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CommitTransactionAsync());

        Assert.Contains(nameof(TestDbContext), ex.Message);
        Assert.Contains("AddTnziDbContext", ex.Message);
    }

    /// <summary>同一守卫也盖住 <c>SaveChangesAsync</c>（<c>FlushAsync</c> 背后）：它此前返回 0 且什么都不保存。</summary>
    [Fact]
    public async Task ContextRegisteredOutsideTheFunnel_FailsClosedOnSaveChanges()
    {
        var provider = BuildProvider(services =>
            services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection)));

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveChangesAsync());
    }

    /// <summary>
    /// 对照组：宿主里根本没有 DbContext（纯缓存 / 纯消息的轻宿主）时不是错误，管理器只把「没有可提交的上下文」说出来。
    /// </summary>
    [Fact]
    public async Task NoDbContextAtAll_IsLoggedAsAWarning()
    {
        var sink = new CapturingLoggerProvider();
        var provider = BuildProvider(services => services.AddLogging(b => b.AddProvider(sink)), ensureCreated: false);

        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        manager.EnableTransaction();
        await manager.CommitTransactionAsync();

        Assert.Contains(sink.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("No DbContext", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>最小日志捕获器：只留级别与消息。</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entries)
            {
                entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
