
namespace Tnzi.Identity.Tests.Integration;

/// <summary>
/// Identity 模块集成测试基类 - 简化版本
/// </summary>
public class IntegrationTestBase : IntegratedTestBase<IdentityTestDbContext>, IDisposable
{
    protected IntegrationTestBase()
    {
    }
}

/// <summary>
/// Identity 测试用 DbContext
/// </summary>
public class IdentityTestDbContext : TnziDbContext<IdentityTestDbContext>
{
    public IdentityTestDbContext(
        DbContextOptions<IdentityTestDbContext> options,
        Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<LoginLog> LoginLogs => Set<LoginLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 Identity 实体配置
        modelBuilder.ApplyConfiguration(new Tnzi.Identity.Entities.Configs.UserConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Identity.Entities.Configs.OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Identity.Entities.Configs.LoginLogConfiguration());

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}