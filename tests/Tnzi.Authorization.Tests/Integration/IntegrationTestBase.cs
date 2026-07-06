
namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// Authorization 模块集成测试基类
/// </summary>
public class IntegrationTestBase : IntegratedTestBase<AuthorizationTestDbContext>, IDisposable
{
    protected IntegrationTestBase()
    {
    }
}

/// <summary>
/// Authorization 测试用 DbContext
/// </summary>
public class AuthorizationTestDbContext : TnziDbContext<AuthorizationTestDbContext>
{
    public AuthorizationTestDbContext(
        DbContextOptions<AuthorizationTestDbContext> options,
        Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<FunctionModule> FunctionModules => Set<FunctionModule>();
    public DbSet<ModuleFunction> ModuleFunctions => Set<ModuleFunction>();
    public DbSet<ModuleRole> ModuleRoles => Set<ModuleRole>();
    public DbSet<ModuleUser> ModuleUsers => Set<ModuleUser>();
    public DbSet<RoleFunction> RoleFunctions => Set<RoleFunction>();
    public DbSet<EntityInfo> EntityInfos => Set<EntityInfo>();
    public DbSet<EntityRole> EntityRoles => Set<EntityRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 Authorization 实体配置
        modelBuilder.ApplyConfiguration(new Entities.Configs.FunctionModuleConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.ModuleFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.ModuleRoleConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.ModuleUserConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.RoleFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityInfoConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityRoleConfiguration());

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}