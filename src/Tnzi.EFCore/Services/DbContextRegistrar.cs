
namespace Tnzi.EFCore.Services;

/// <summary>
/// DbContext 注册服务实现
/// </summary>
public class DbContextRegistrar : IDbContextRegistrar
{
    /// <summary>
    /// 注册 DbContext 到服务集合
    /// </summary>
    public void RegisterDbContext(
        IServiceCollection services,
        DbContextDiscoveryItem item,
        bool isPrimary,
        IConfiguration? configuration = null,
        ILogger? logger = null)
    {
        var config = item.Configuration;
        var dbContextType = item.DbContextType;

        try
        {
            // 调用 AddTnziDbContext（通过反射调用泛型方法）
            var addMethod = typeof(TnziEFCoreExtensions)
                .GetMethod(nameof(TnziEFCoreExtensions.AddTnziDbContext))!
                .MakeGenericMethod(dbContextType);

            // 护栏：重试型 execution strategy 与框架 UnitOfWork 的手动事务互斥（EF Core 硬约束）。
            // 在启动路径（每个 DbContext 注册时）显式告警，避免运行时才暴露冲突。
            var providerOptions = config.BuildProviderConfigureOptions();
            if (providerOptions.ConflictsWithUnitOfWorkTransaction)
            {
                logger?.LogWarning(
                    "DbContext '{Name}' has EnableRetryOnFailure=true. The retrying execution strategy is mutually " +
                    "exclusive with UnitOfWork's manual transactions: any UoW BeginTransaction on this DbContext will " +
                    "throw at runtime. Disable the global UnitOfWork transaction for this DbContext, or wrap writes in " +
                    "an explicit IExecutionStrategy.ExecuteAsync(...) block. See docs/modules/efcore.md (Retry & Execution Strategy).",
                    config.Name);
            }

            // 创建配置选项的 Action
            // 使用 GetEffectiveConnectionString() 获取合并连接池配置和环境变量展开后的连接字符串
            Action<DbContextOptionsBuilder> optionsAction = builder =>
            {
                var effectiveConnectionString = config.GetEffectiveConnectionString(configuration, logger);
                DatabaseProviderFactory.Configure(builder, effectiveConnectionString, config.Provider, providerOptions);
            };

            // 调用 AddTnziDbContext，传递 isPrimary 参数
            addMethod.Invoke(null, new object[] { services, optionsAction, isPrimary });

            // 如果是主 DbContext，注册为基类 DbContext
            if (isPrimary)
            {
                // 检查是否已注册基类 DbContext
                if (!services.Any(s => s.ServiceType == typeof(DbContext) && s.Lifetime == ServiceLifetime.Scoped))
                {
                    services.AddScoped(typeof(DbContext), provider => provider.GetRequiredService(dbContextType));
                }
            }

            logger?.LogInformation(
                "Registered DbContext '{Name}' ({DbContextType}) with provider {Provider}{Primary}",
                config.Name,
                dbContextType.Name,
                config.Provider,
                isPrimary ? " [PRIMARY]" : "");
        }
        catch (Exception ex)
        {
            // 注册失败时抛出异常，而不是仅记录日志
            var errorMessage = $"Failed to register DbContext '{config.Name}' ({dbContextType.FullName}) from configuration.\n" +
                             $"Error: {ex.Message}\n\n" +
                             $"Please check:\n" +
                             $"1. DbContext type '{dbContextType.FullName}' is correct\n" +
                             $"2. ConnectionString is valid\n" +
                             $"3. Database provider package is installed";

            logger?.LogError(ex, errorMessage);
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    /// <summary>
    /// 批量注册 DbContext
    /// </summary>
    public void RegisterDbContexts(
        IServiceCollection services,
        DbContextDiscoveryResult result,
        IConfiguration? configuration = null,
        ILogger? logger = null)
    {
        if (!result.Success || result.PrimaryConfiguration == null)
        {
            // 如果没有有效配置，不应该到达这里（应该在 EFCoreModule 中已抛出异常）
            // 但为了防御性编程，这里也检查一下
            return;
        }

        // 先注册主 DbContext
        RegisterDbContext(services, result.PrimaryConfiguration, true, configuration, logger);

        // 然后注册其他 DbContext
        foreach (var item in result.SecondaryConfigurations)
        {
            RegisterDbContext(services, item, false, configuration, logger);
        }
    }
}