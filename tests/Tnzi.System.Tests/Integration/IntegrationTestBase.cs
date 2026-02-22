
namespace Tnzi.System.Tests.Integration;

/// <summary>
/// System 模块集成测试基类
/// </summary>
public class IntegrationTestBase : IntegratedTestBase<SystemTestDbContext>, IDisposable
{
    protected IntegrationTestBase()
    {
    }
}

/// <summary>
/// System 测试用 DbContext
/// </summary>
public class SystemTestDbContext : TnziDbContext<SystemTestDbContext>
{
    public SystemTestDbContext(
        DbContextOptions<SystemTestDbContext> options,
        Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 System 实体配置
        modelBuilder.ApplyConfiguration(new Entities.Configs.MenuConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.AccessLogConfiguration());

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}