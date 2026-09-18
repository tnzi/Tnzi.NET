
namespace Tnzi.TestBase;

/// <summary>
/// 集成测试基类，提供基于 SQLite 内存数据库的 DbContext 和 ServiceProvider
/// </summary>
/// <typeparam name="TDbContext">依赖的 DbContext 类型</typeparam>
public abstract class IntegratedTestBase<TDbContext> : IDisposable 
    where TDbContext : DbContext
{
    private readonly SqliteConnection _connection;
    protected readonly IServiceProvider ServiceProvider;
    protected readonly TDbContext DbContext;

    protected IntegratedTestBase()
    {
        var services = new ServiceCollection();

        // 1. 配置默认 Mock (CurrentUser, CurrentTenant)
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(TestHelper.DefaultTestUserId);
        currentUserMock.Setup(m => m.UserName).Returns(TestHelper.DefaultTestUserName);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(true);
        currentUserMock.Setup(m => m.Roles).Returns(TestHelper.DefaultTestUserRoles);
        currentUserMock.Setup(m => m.IsInRole(It.IsAny<string>())).Returns((string r) => r == "Admin");
        services.AddScoped(_ => currentUserMock.Object);

        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.Setup(t => t.Id).Returns((Guid?)null);
        services.AddScoped(_ => currentTenantMock.Object);

        // 2. 配置日志
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // 3. 配置 SQLite 内存数据库 (必须手动管理连接以保持内存库不被销毁)
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<TDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.EnableSensitiveDataLogging();
            ConfigureDbContextOptions(options);
        });

        // 4. 允许子类配置额外服务 (如仓储、业务服务)
        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();
        DbContext = ServiceProvider.GetRequiredService<TDbContext>();

        // 5. 初始化数据库结构
        DbContext.Database.EnsureCreated();
    }

    /// <summary>
    /// 在子类中实现此方法以注册特定模块的服务
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// 在子类中覆写以补充 DbContext 选项（在 SQLite 连接与敏感数据日志之后调用）。
    /// </summary>
    /// <remarks>
    /// ★ 多租户消费方必须在这里调 <c>options.UseTnziMultiTenancy(true)</c>：框架的多租户开关只经
    /// <c>DbContextOptions</c> 到达消费方 DbContext（构造函数上没有它的形参），而本基类用裸 <c>AddDbContext</c>，
    /// 不覆写则集成测试全部跑在单租户模型上 —— 没有 <c>TenantId</c> 列、没有租户过滤器，
    /// 与应用的 <c>MultiTenancy:Enabled</c> 无关。本基类只依赖核心包，故这里给的是钩子而不是一个开关值。
    /// </remarks>
    protected virtual void ConfigureDbContextOptions(DbContextOptionsBuilder options)
    {
    }

    /// <summary>
    /// 为 SQLite 数据库应用 UTC 时间转换器（通常在 DbContext.OnModelCreating 中通过遍历属性调用）
    /// </summary>
    protected void ApplySqliteUtcDateTimeConverter(ModelBuilder modelBuilder)
    {
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, DbContext.Database.ProviderName);
    }

    public virtual void Dispose()
    {
        try
        {
            DbContext?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
            (ServiceProvider as IDisposable)?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
        GC.SuppressFinalize(this);
    }
}