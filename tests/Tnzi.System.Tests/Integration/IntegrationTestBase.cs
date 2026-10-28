
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
    /// <param name="options">DbContext 选项。</param>
    /// <param name="currentUser">当前用户。</param>
    /// <param name="multiTenancyOptions">
    /// 多租户开关。默认关闭（与生产默认一致，<c>AccessLog.TenantId</c> 被 Ignore）；
    /// 租户收口的测试在 <c>ConfigureServices</c> 里 <c>Configure&lt;MultiTenancyOptions&gt;</c> 打开它，列才会被映射。
    /// </param>
    public SystemTestDbContext(
        DbContextOptions<SystemTestDbContext> options,
        Security.Claims.ICurrentUser currentUser,
        IOptions<Tnzi.MultiTenancy.MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // 模型缓存键要含多租户开关：否则同一测试进程里先构造的单租户模型（TenantId 被 Ignore）
        // 会被开启多租户的测试类原样复用，租户谓词在那张模型上翻译失败。生产经 AddTnziDbContext 注册同一个工厂。
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>();
        base.OnConfiguring(optionsBuilder);
    }

    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用 System 实体配置。★ 经 RegisterTo(…, this) 而不是裸 ApplyConfiguration：
        // 配置类要从 DbContext 读多租户开关来决定 TenantId 是建索引还是 Ignore，裸 ApplyConfiguration 拿不到 DbContext。
        new Entities.Configs.AccessLogConfiguration().RegisterTo(modelBuilder, this);

        base.OnModelCreating(modelBuilder);

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}