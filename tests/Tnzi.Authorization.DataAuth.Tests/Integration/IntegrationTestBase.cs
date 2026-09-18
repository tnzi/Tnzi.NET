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

    /// <summary>
    /// 被过滤的业务实体（住在<b>测试程序集</b>里）。行级过滤要真的挂到一张表上才测得出来；
    /// 而它必须住在本模块之外，因为过滤器保存期的类型解析曾经只在本模块自己的程序集里找 ——
    /// 用本模块的实体（<see cref="EntityInfo"/>）当靶子修前也绿，是假阳性。
    /// </summary>
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityInfoConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.EntityRoleConfiguration());

        modelBuilder.Entity<Ticket>(b =>
        {
            b.ToTable("Test_Ticket");
            b.HasKey(t => t.Id);
        });

        base.OnModelCreating(modelBuilder);

        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>消费方业务实体的替身：一张有 OwnerId 的工单表。</summary>
public class Ticket : Tnzi.Domain.Entities.EntityBase<Guid>
{
    public Guid OwnerId { get; set; }

    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 雪花 long 主键的消费方实体替身。<b>刻意不映射进 DbContext</b>：<c>admin/data-auth/check</c> 的判定接受的是
/// <c>Guid entityId</c>，对非 Guid 主键的登记它做不了 —— 要守的是「答 501 而不是 500」，那一步发生在碰仓储之前；
/// 类型解析走 <c>ResolveEntityType</c> 的程序集扫描兜底，不需要在模型里。
/// </summary>
public class LongTicket : Tnzi.Domain.Entities.EntityBase<long>
{
    public Guid OwnerId { get; set; }
}
