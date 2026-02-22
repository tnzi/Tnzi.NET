using IDatabaseProvider = Tnzi.EFCore.Dapper.Providers.IDatabaseProvider;

namespace Tnzi.EFCore;

/// <summary>
/// Tnzi EFCore 扩展方法
/// 提供 DbContext 注册和 Repository 自动发现功能
/// </summary>
public static class TnziEFCoreExtensions
{
    /// <summary>
    /// 添加 Tnzi DbContext，自动配置 Repository、验证码服务等
    /// </summary>
    /// <typeparam name="TDbContext">DbContext 类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="optionsAction">DbContext 配置选项</param>
    /// <param name="isPrimary">是否为主 DbContext。只有主 DbContext 才会注册 DbContextType 为 null 的实体</param>
    public static void AddTnziDbContext<TDbContext>(this IServiceCollection services, Action<DbContextOptionsBuilder>? optionsAction = null, bool isPrimary = false)
        where TDbContext : DbContext
    {
        // 包装用户的配置，添加慢查询拦截器
        services.AddDbContext<TDbContext>((serviceProvider, options) =>
        {
            // 先执行用户的配置
            optionsAction?.Invoke(options);

            // 添加慢查询拦截器（如果已注册）
            var interceptor = serviceProvider.GetService<Interceptors.SlowQueryLoggingInterceptor>();
            if (interceptor != null)
            {
                options.AddInterceptors(interceptor);
            }
        });
        services.AddScoped<IDbInitializer, EFCoreDbInitializer<TDbContext>>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<TDbContext>>();

        // 注意：基类 DbContext 的注册现在由自动发现机制处理
        // 只有在明确标记为主 DbContext 时才会注册为基类
        // 如果需要手动注册基类，请在调用 AddTnziDbContext 后手动添加：
        // services.AddScoped<DbContext>(provider => provider.GetRequiredService<TDbContext>());



        // 使用 RepositoryRegistrar 注册 Repository
        var registrar = new Services.RepositoryRegistrar();
        registrar.RegisterRepositoriesFromDbContext(services, typeof(TDbContext));
        registrar.RegisterRepositoriesFromEntityRegisters(services, typeof(TDbContext), isPrimary);

        // 注册 Dapper 支持
        RegisterDapperServices(services, typeof(TDbContext));
    }

    /// <summary>
    /// 注册 Dapper 相关服务
    /// 使用 TryAddScoped 避免多 DbContext 场景下重复注册（只注册第一个）
    /// 多 DbContext 路由通过 DapperExecutor 实现，不依赖单一的 IDapperService 注册
    /// </summary>
    private static void RegisterDapperServices(IServiceCollection services, Type dbContextType)
    {
        // 注册 IDatabaseProvider（TryAdd 确保只注册一次，对应首个 DbContext）
        services.TryAddScoped<IDatabaseProvider>(sp =>
        {
            var configuration = sp.GetService<IConfiguration>();
            var dbContext = sp.GetRequiredService(dbContextType) as DbContext
                ?? throw new InvalidOperationException($"Type {dbContextType.Name} is not a DbContext");
            return DapperDatabaseProviderFactory.CreateFromDbContext(dbContext, configuration);
        });

        // 注册 IDapperService（TryAdd 确保只注册一次，对应首个 DbContext）
        // 多 DbContext 场景下，DapperExecutor 会根据实体类型创建正确的 DapperService 实例
        services.TryAddScoped<IDapperService>(sp =>
        {
            var dbContext = sp.GetRequiredService(dbContextType) as DbContext
                ?? throw new InvalidOperationException($"Type {dbContextType.Name} is not a DbContext");
            var databaseProvider = sp.GetRequiredService<IDatabaseProvider>();
            var logger = sp.GetRequiredService<ILogger<DapperService>>();
            return new DapperService(dbContext, databaseProvider, logger);
        });

        // 注册 Dapper Executor 工厂（TryAdd 确保只注册一次）
        services.TryAddScoped<DapperExecutorFactory>();
    }
}
