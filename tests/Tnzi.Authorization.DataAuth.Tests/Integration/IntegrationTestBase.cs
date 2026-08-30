namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 行级数据授权子模块的集成测试基类。
/// </summary>
/// <remarks>
/// 只映射本模块自己的两张表：拆分后 <c>Auth_EntityInfo</c> / <c>Auth_EntityRole</c> 与
/// 功能级授权的四张表分属两个程序集，测试夹具也就没有理由再把它们拉到一起。
/// </remarks>
public class IntegrationTestBase : IntegratedTestBase<DataAuthTestDbContext>, IDisposable
{
    protected IntegrationTestBase()
    {
    }
}

/// <summary>
/// 行级数据授权测试用 DbContext。
/// </summary>
public class DataAuthTestDbContext : TnziDbContext<DataAuthTestDbContext>
{
    public DataAuthTestDbContext(
        DbContextOptions<DataAuthTestDbContext> options,
        Tnzi.Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<EntityInfo> EntityInfos => Set<EntityInfo>();
    public DbSet<EntityRole> EntityRoles => Set<EntityRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityInfoConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityRoleConfiguration());

        base.OnModelCreating(modelBuilder);

        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
