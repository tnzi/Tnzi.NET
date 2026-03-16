namespace Tnzi.AI.Tests;

/// <summary>
/// AI 分析服务集成测试 DbContext
/// </summary>
public class AIAnalyticsDbContext : TnziDbContext<AIAnalyticsDbContext>
{
    public AIAnalyticsDbContext(
        DbContextOptions<AIAnalyticsDbContext> options,
        Tnzi.Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.UsageLogConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// UsageAnalyticsService 集成测试基类
/// </summary>
public abstract class AIAnalyticsTestBase : IntegratedTestBase<AIAnalyticsDbContext>
{
    protected readonly IUsageAnalyticsService Service;

    protected AIAnalyticsTestBase()
    {
        Service = ServiceProvider.GetRequiredService<IUsageAnalyticsService>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        services.AddScoped<IRepository<UsageLog, Guid>,
            EFCoreRepository<AIAnalyticsDbContext, UsageLog, Guid>>();
        services.AddScoped<IRepository<Agent, Guid>,
            EFCoreRepository<AIAnalyticsDbContext, Agent, Guid>>();
        services.AddScoped<IUsageAnalyticsService, UsageAnalyticsService>();
    }

    protected async Task SeedLogsAsync(IEnumerable<UsageLog> logs)
    {
        DbContext.Set<UsageLog>().AddRange(logs);
        await DbContext.SaveChangesAsync();
    }

    protected async Task SeedAgentsAsync(IEnumerable<Agent> agents)
    {
        DbContext.Set<Agent>().AddRange(agents);
        await DbContext.SaveChangesAsync();
    }
}
