
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