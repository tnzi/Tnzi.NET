
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
    public DbSet<RoleFunction> RoleFunctions => Set<RoleFunction>();
    public DbSet<UserFunction> UserFunctions => Set<UserFunction>();
    public DbSet<EntityInfo> EntityInfos => Set<EntityInfo>();
    public DbSet<EntityRole> EntityRoles => Set<EntityRole>();

    /// <summary>
    /// Identity 角色表(最小映射)——委托护栏的超管角色保护按角色名判定,
    /// FunctionAuthorizationService 经可选角色仓储读取。
    /// </summary>
    public DbSet<Tnzi.Identity.Entities.Role> IdentityRoles => Set<Tnzi.Identity.Entities.Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 Authorization 实体配置
        modelBuilder.ApplyConfiguration(new Entities.Configs.FunctionModuleConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.ModuleFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.RoleFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.UserFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityInfoConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityRoleConfiguration());

        // Identity Role 最小按约定映射(仅委托护栏测试用,不拉入 Identity 全模型)。
        modelBuilder.Entity<Tnzi.Identity.Entities.Role>(b =>
        {
            b.ToTable("Identity_Role");
            b.HasKey(r => r.Id);
        });

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}