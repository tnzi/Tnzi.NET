
namespace Tnzi.EFCore;

/// <summary>
/// EF Core 数据访问模块
/// 配置路径：Database
/// </summary>
public class EFCoreModule : TnziInfrastructureModule
{
    /// <summary>
    /// EF Core 在缓存和事件总线之后加载
    /// </summary>
    public override int LoadOrder => 20;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;

        // 注册配置选项
        context.Services.Configure<DatabaseOptions>(configuration.GetSection("Database"));

        // 注册 EFCore 配置选项并启用启动时验证
        context.Services.AddTnziOptions<Options.EFCoreOptions, Options.EFCoreOptionsValidator>(configuration);

        // 注册 Outbox 配置选项并启用启动时验证（section 路径由 [ConfigSection] 派生）
        context.Services.AddTnziOptions<OutboxOptions, OutboxOptionsValidator>(configuration);

        // 注册多租户开关
        context.Services.AddTnziOptions<MultiTenancyOptions>(configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Configuration;

        // 注册 EntityManager
        services.AddSingleton<IEntityManager, EntityManager>();

        // 注册 TableNamePrefixConfiguration（使用工厂方式，以便获取 IModuleContainer）
        services.AddSingleton<IEntityBatchConfiguration>(serviceProvider =>
        {
            var moduleContainer = serviceProvider.GetService<IModuleContainer>();
            if (moduleContainer == null)
            {
                var logger = serviceProvider.GetService<ILogger<EFCoreModule>>();
                logger?.LogWarning("IModuleContainer is NULL in EFCoreModule factory. Table prefixes will not work.");
            }
            return new TableNamePrefixConfiguration(moduleContainer);
        });

        // 注册 UnitOfWorkManager（用于管理多个 DbContext 的工作单元）
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // 注册事务提交后操作队列（用于 Post-Commit 事件发布等场景）
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();

        // 注册数据过滤器管理器
        services.AddScoped<IDataFilterManager, Data.DataFilterManager>();

        // 注册当前租户服务
        services.AddScoped<Tnzi.MultiTenancy.ICurrentTenant, Tnzi.MultiTenancy.CurrentTenant>();

        // 如果 ICurrentUser 还没有注册，注册一个默认实现（用于 Design-Time 和早期初始化）
        // 注意：AspNetCoreModule 会注册 HttpContextCurrentUser，这里只是作为后备
        // 使用 TryAdd 确保不会覆盖 AspNetCoreModule 的注册
        services.TryAddTransient<Tnzi.Security.Claims.ICurrentUser, DesignTimeCurrentUser>();

        // 注册新的服务类
        services.AddSingleton<Services.IDbContextDiscoveryService, Services.DbContextDiscoveryService>();
        services.AddSingleton<Services.IDbContextRegistrar, Services.DbContextRegistrar>();
        services.AddSingleton<Services.IDataSeederManager, Services.DataSeederManager>();
        services.AddSingleton<Services.IRepositoryRegistrar, Services.RepositoryRegistrar>();

        // 注册数据库迁移器（TryAdd 确保不覆盖用户自定义实现）
        services.TryAddScoped<IDbMigrator, Services.EfCoreDbMigrator>();

        // 注册慢查询日志拦截器（Scoped 以支持 Logger 注入）
        services.AddScoped<Interceptors.SlowQueryLoggingInterceptor>();

        // 注册 Dapper 相关服务
        services.AddScoped<Dapper.DapperExecutorFactory>();

        // 读取 Outbox 配置，决定是否启用
        var outboxOptions = configuration.GetSection("EFCore:Outbox").Get<OutboxOptions>() ?? new OutboxOptions();

        // 同步静态标志，控制 OutboxMessage 实体是否参与 DbContext 模型构建
        // 必须在 EntityManager.Initialize() 之前设置（Initialize 在 OnApplicationInitializationAsync 中调用）
        Outbox.Configs.OutboxMessageConfiguration.OutboxEnabled = outboxOptions.Enabled;

        if (outboxOptions.Enabled)
        {
            // 注册 Outbox 事件存储（TryAdd 确保不会覆盖用户自定义实现）
            services.TryAddScoped<IEventStore, EfCoreEventStore>();
            services.AddHostedService<OutboxRelayBackgroundService>();
        }

        // 获取配置选项
        var options = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();

        // DbContext 注册逻辑
        var discoveryService = new Services.DbContextDiscoveryService();
        var registrar = new Services.DbContextRegistrar();

        // 从配置文件发现 DbContext（无论 AutoDiscoverDbContexts 是 true 还是 false）
        var result = discoveryService.DiscoverDbContexts(options, logger: null);

        if (options.AutoDiscoverDbContexts)
        {
            // 自动发现模式：如果启用了自动发现但没有有效配置，抛出异常
            if (!result.Success)
            {
                if (options.DbContexts == null || options.DbContexts.Count == 0)
                {
                    // 场景 1: 配置为空
                    throw new InvalidOperationException(
                        "AutoDiscoverDbContexts is enabled but no DbContexts configuration found in 'Database:DbContexts' section.\n\n" +
                        "To fix this:\n" +
                        "1. Add DbContexts configuration to your appsettings.json:\n" +
                        "   {\n" +
                        "     \"Database\": {\n" +
                        "       \"DbContexts\": [\n" +
                        "         {\n" +
                        "           \"Name\": \"Default\",\n" +
                        "           \"DbContextType\": \"YourApp.Data.DefaultDbContext, YourApp\",\n" +
                        "           \"ConnectionString\": \"Server=localhost;Database=YourDb;...\",\n" +
                        "           \"Provider\": \"SqlServer\"\n" +
                        "         }\n" +
                        "       ]\n" +
                        "     }\n" +
                        "   }\n" +
                        "2. Or set AutoDiscoverDbContexts to false if you want to register DbContexts manually in code.");
                }
                else if (result.HasInvalidConfigurations)
                {
                    // 场景 2: 有配置但都无效
                    var errorDetails = string.Join("\n", result.ValidationErrors.Select(kvp =>
                        $"- Name: '{kvp.Key}', Error: {kvp.Value}"));

                    // 检查是否有 DbContextType 为空的错误，提供更具体的指导
                    var hasEmptyDbContextType = result.ValidationErrors.Any(kvp =>
                        kvp.Value.Contains("DbContextType is empty", StringComparison.OrdinalIgnoreCase));

                    var fixGuidance = hasEmptyDbContextType
                        ? "To fix 'DbContextType is empty' error:\n" +
                          "1. Create a DbContext class in your project (e.g., Data/DefaultDbContext.cs):\n" +
                          "   public class DefaultDbContext : IdentityDbContext<DefaultDbContext> { ... }\n" +
                          "2. Add DbContextType to your configuration:\n" +
                          "   \"DbContextType\": \"YourApp.Data.DefaultDbContext, YourApp\"\n\n" +
                          "Or set AutoDiscoverDbContexts to false if you want to register DbContexts manually in code.\n\n"
                        : "";

                    throw new InvalidOperationException(
                        $"AutoDiscoverDbContexts is enabled but all DbContext configurations are invalid.\n\n" +
                        $"Invalid configurations:\n{errorDetails}\n\n" +
                        fixGuidance +
                        $"Please ensure:\n" +
                        $"1. DbContextType is correct and the assembly is loaded\n" +
                        $"2. DbContext class exists and inherits from DbContext\n" +
                        $"3. ConnectionString is not empty\n" +
                        $"4. Provider is valid (SqlServer, PostgreSQL, MySql, Sqlite)");
                }
            }
        }
        else
        {
            // 手动模式（AutoDiscoverDbContexts = false）
            // 如果配置了 DbContexts，仍然会从配置文件读取并注册
            // 如果没有配置或配置无效，不会抛出异常（允许完全手动注册）
            if (!result.Success)
            {
                if (options.DbContexts != null && options.DbContexts.Count > 0 && result.HasInvalidConfigurations)
                {
                    // 配置了但无效，记录警告但不抛出异常（允许用户在代码中手动注册）
                    // 注意：这里无法记录日志，因为我们没有 logger
                    // 运行时错误会在尝试使用 DbContext 时暴露
                }
                // 如果没有配置 DbContexts，不做任何事情，允许用户完全手动注册
            }
        }

        // 如果从配置文件发现了有效的 DbContext，则注册它们
        // 这适用于两种模式：
        // 1. AutoDiscoverDbContexts = true：必须成功，否则已在上方抛出异常
        // 2. AutoDiscoverDbContexts = false：如果有有效配置则注册，否则跳过（允许手动注册）
        if (result.Success && result.PrimaryConfiguration != null)
        {
            registrar.RegisterDbContexts(services, result, configuration, logger: null);
        }

        // 使用新服务自动注册 DataSeeders
        if (options.AutoRegisterDataSeeders)
        {
            var seederManager = new Services.DataSeederManager();
            seederManager.RegisterSeeders(services);
        }

        // 注意：Repository 的注册将在 AddTnziDbContext 被调用时进行

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var serviceProvider = context.ServiceProvider;

        // 初始化 EntityManager
        var entityManager = serviceProvider.GetRequiredService<IEntityManager>();
        entityManager.Initialize();

        var logger = serviceProvider.GetService<ILogger<EFCoreModule>>();
        logger?.LogDebug(
            "EntityManager initialized with {Count} discovered entities. Repository registration must be completed during ConfigureServicesAsync.",
            entityManager.GetAllEntityTypes().Length);

        return Task.CompletedTask;
    }
}
