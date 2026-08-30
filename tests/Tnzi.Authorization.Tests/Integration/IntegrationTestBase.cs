
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
        Tnzi.Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<FunctionModule> FunctionModules => Set<FunctionModule>();
    public DbSet<ModuleFunction> ModuleFunctions => Set<ModuleFunction>();
    public DbSet<RoleFunction> RoleFunctions => Set<RoleFunction>();
    public DbSet<UserFunction> UserFunctions => Set<UserFunction>();
    public DbSet<DualControlRequest> DualControlRequests => Set<DualControlRequest>();

    /// <summary>
    /// Identity 角色表(最小映射)——委托护栏的超管角色保护按角色名判定,
    /// FunctionAuthorizationService 经可选角色仓储读取。
    /// </summary>
    public DbSet<Tnzi.Identity.Entities.Role> IdentityRoles => Set<Tnzi.Identity.Entities.Role>();

    /// <summary>
    /// Identity 用户表(最小映射)——双人授权待办面把发起人/审批人 Guid 换成用户名,
    /// DualControlService 经可选用户仓储读取。
    /// </summary>
    public DbSet<Tnzi.Identity.Entities.User> IdentityUsers => Set<Tnzi.Identity.Entities.User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 Authorization 实体配置
        modelBuilder.ApplyConfiguration(new Entities.Configs.FunctionModuleConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.ModuleFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.RoleFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.UserFunctionConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.DualControlRequestConfiguration());

        // Identity Role 最小按约定映射(仅委托护栏测试用,不拉入 Identity 全模型)。
        modelBuilder.Entity<Tnzi.Identity.Entities.Role>(b =>
        {
            b.ToTable("Identity_Role");
            b.HasKey(r => r.Id);
        });

        // Identity User 同理(双人授权姓名解析用)。
        // 组织导航自 2026-08-29 起不在 User 上了(随 Tnzi.Identity.Organization 拆出),
        // 所以这里不再需要显式忽略它 —— OrganizationId 只是一个裸列,不会把别的模型拉进来。
        modelBuilder.Entity<Tnzi.Identity.Entities.User>(b =>
        {
            b.ToTable("Identity_User");
            b.HasKey(u => u.Id);
        });

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}