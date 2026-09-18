
namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 审计模块测试用 DbContext
/// </summary>
public class AuditTestDbContext : TnziDbContext<AuditTestDbContext>
{
    /// <param name="options">DbContext 选项。</param>
    /// <param name="currentUser">当前用户。</param>
    /// <param name="multiTenancyOptions">
    /// 多租户开关。默认关闭（与生产一致，三张审计表的 <c>TenantId</c> 被 Ignore）；
    /// 租户收口的测试在 <c>ConfigureServices</c> 里 <c>Configure&lt;MultiTenancyOptions&gt;</c> 打开它，列才会被映射。
    /// </param>
    public AuditTestDbContext(
        DbContextOptions<AuditTestDbContext> options,
        Tnzi.Security.Claims.ICurrentUser currentUser,
        IOptions<Tnzi.MultiTenancy.MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // 生产经 AddTnziDbContext 注册同一个工厂：模型缓存键要含多租户开关，否则同一测试进程里
        // 第一个构造的 AuditTestDbContext 模型（多租户关闭、TenantId 被 Ignore）会被之后
        // 开启多租户的测试类原样复用，租户谓词在那张模型上翻译失败。
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>();
        base.OnConfiguring(optionsBuilder);
    }

    public DbSet<AuditOperation> AuditOperations => Set<AuditOperation>();
    public DbSet<AuditEntityEntry> AuditEntityEntries => Set<AuditEntityEntry>();
    public DbSet<AuditPropertyEntry> AuditPropertyEntries => Set<AuditPropertyEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用审计模块的实体配置。★ 经 RegisterTo(…, this) 而不是裸 ApplyConfiguration：
        // 配置类要从 DbContext 读多租户开关来决定 TenantId 是建索引还是 Ignore，
        // 裸 ApplyConfiguration 拿不到 DbContext，开关恒读成关闭。
        new Tnzi.Audit.Entities.Configs.AuditOperationConfiguration().RegisterTo(modelBuilder, this);
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditEntityEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditPropertyEntryConfiguration());

        // 保留策略的被试实体：只存在于测试程序集，用来验证「对任意实体按策略销毁」。
        // 继承 FullAuditedEntity 是刻意的——它带 ISoftDelete，正好覆盖「销毁必须真删、
        // 不能退化成把 IsDeleted 置真」这条最容易做错的要求。
        // ★必须在 base 之前配置：base.OnModelCreating 遍历**当时模型里已有的**实体来挂软删除
        //   全局过滤器，base 之后才加入模型的实体拿不到过滤器——那样「销毁要连已软删的行一起删」
        //   这条断言会在一个根本没有过滤器的实体上永远通过（变异验证时实测到的假绿）。
        modelBuilder.Entity<RetentionTestRecord>(b =>
        {
            b.ToTable("RetentionTestRecord");
            b.HasKey(e => e.Id);
            b.Property(e => e.Category).HasMaxLength(64);
        });

        // 带租户维度的被试实体：按租户逐个销毁、证书记在被销毁数据所属租户名下，只有它走得到。
        // 同样必须在 base 之前配置（理由同上）；多租户关闭的 DbContext 里 base 会把它当普通实体。
        modelBuilder.Entity<TenantRetentionTestRecord>(b =>
        {
            b.ToTable("TenantRetentionTestRecord");
            b.HasKey(e => e.Id);
            b.Property(e => e.Category).HasMaxLength(64);
        });

        base.OnModelCreating(modelBuilder);

        // 记录级读取审计表：生产环境下建不建取决于 Audit:RecordAccess:Enabled，
        // 未启用时实体被排除出迁移（EnsureCreated 也不会建）。
        // 测试 DbContext 不经 AddDbContext 构建，拿不到应用服务提供程序，
        // 自动发现那次必然判定为未启用并打上排除标记 —— 而且在打标记那一行就提前返回了，
        // 列宽与 (UserId, Sequence) 唯一索引一条都没配。★ 没有那条唯一索引，哈希链的并发冲突
        // 在测试库里根本不会发生，「冲突后重读链尾重试」这条路径就永远测不到。
        // 所以这里用显式 enabled 构造把完整配置再跑一遍，再撤销排除标记并补上模块表前缀
        //（base 里的前缀约定只作用于它自己那一轮配置）。
        new Tnzi.Audit.Entities.Configs.AuditRecordAccessConfiguration(enabled: true).RegisterTo(modelBuilder, this);
        modelBuilder.Entity<AuditRecordAccess>()
            .ToTable("Audit_RecordAccess", t => t.ExcludeFromMigrations(false));

        // 销毁证明表同理（Audit:DataDestruction:Enabled 在测试里读不到）。此前只调了 ToTable，
        // 实体按约定映射 —— TenantId 无论开关都成了一列，「多租户关闭时谓词不得引用它」在这里守不住。
        new Tnzi.Audit.Entities.Configs.AuditDataDestructionConfiguration(enabled: true).RegisterTo(modelBuilder, this);
        modelBuilder.Entity<AuditDataDestruction>()
            .ToTable("Audit_DataDestruction", t => t.ExcludeFromMigrations(false));

        // 应用 SQLite UTC DateTime 转换器
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 集成测试基类
/// </summary>
public abstract class IntegrationTestBase : IntegratedTestBase<AuditTestDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // 初始化 Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        // 注册仓储
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<AuditOperation, Guid>,
            Tnzi.EFCore.EFCoreRepository<AuditTestDbContext, AuditOperation, Guid>>();
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<AuditEntityEntry, Guid>,
            Tnzi.EFCore.EFCoreRepository<AuditTestDbContext, AuditEntityEntry, Guid>>();
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<AuditPropertyEntry, Guid>,
            Tnzi.EFCore.EFCoreRepository<AuditTestDbContext, AuditPropertyEntry, Guid>>();

        // AuditOperationService 注入 IOptionsMonitor<AuditOptions>（RetentionDays 热读）
        services.AddOptions<Tnzi.Audit.Options.AuditOptions>();

        // 保留策略被试实体的仓储。两个接口都要注册：销毁服务按 IRepository<TEntity> 查候选，
        // EFCoreRepository 的具体类型是三泛型的，不注册就解析不到。
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<RetentionTestRecord, Guid>,
            Tnzi.EFCore.EFCoreRepository<AuditTestDbContext, RetentionTestRecord, Guid>>();
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<RetentionTestRecord>>(
            sp => sp.GetRequiredService<Tnzi.Domain.Repositories.IRepository<RetentionTestRecord, Guid>>());
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<TenantRetentionTestRecord, Guid>,
            Tnzi.EFCore.EFCoreRepository<AuditTestDbContext, TenantRetentionTestRecord, Guid>>();
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<TenantRetentionTestRecord>>(
            sp => sp.GetRequiredService<Tnzi.Domain.Repositories.IRepository<TenantRetentionTestRecord, Guid>>());
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<AuditDataDestruction, Guid>,
            Tnzi.EFCore.EFCoreRepository<AuditTestDbContext, AuditDataDestruction, Guid>>();

        // 注册服务
        services.AddScoped<Tnzi.Audit.Services.IAuditOperationService,
            Tnzi.Audit.Services.AuditOperationService>();
        services.AddScoped<Tnzi.Audit.Services.IAuditStore,
            Tnzi.Audit.Services.DatabaseAuditStore>();
    }
}
