
namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 审计模块测试用 DbContext
/// </summary>
public class AuditTestDbContext : TnziDbContext<AuditTestDbContext>
{
    public AuditTestDbContext(
        DbContextOptions<AuditTestDbContext> options,
        Tnzi.Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<AuditOperation> AuditOperations => Set<AuditOperation>();
    public DbSet<AuditEntityEntry> AuditEntityEntries => Set<AuditEntityEntry>();
    public DbSet<AuditPropertyEntry> AuditPropertyEntries => Set<AuditPropertyEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 应用审计模块的实体配置
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditOperationConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditEntityEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditPropertyEntryConfiguration());

        base.OnModelCreating(modelBuilder);

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

        // 注册服务
        services.AddScoped<Tnzi.Audit.Services.IAuditOperationService,
            Tnzi.Audit.Services.AuditOperationService>();
        services.AddScoped<Tnzi.Audit.Services.IAuditStore,
            Tnzi.Audit.Services.DatabaseAuditStore>();
    }
}
