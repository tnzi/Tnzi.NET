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

            // 模型缓存键包含多租户开关，避免单/多租户模型串用
            options.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, Internal.MultiTenancyModelCacheKeyFactory>();

            // 添加慢查询拦截器（如果已注册）
            var interceptor = serviceProvider.GetService<Interceptors.SlowQueryLoggingInterceptor>();
            if (interceptor != null)
            {
                options.AddInterceptors(interceptor);
            }

            // 模块贡献的 EF 拦截器 seam：模块把拦截器注册为 IInterceptor 服务即可挂进
            // 所有 Tnzi DbContext（如 Tnzi.Audit 的实体级审计 SaveChanges 拦截器）。
            // options 生命周期为 Scoped，拦截器可注册为 Scoped 并注入 per-request 服务。
            // 未注册任何 IInterceptor 时（模块未加载）零开销。
            var moduleInterceptors = serviceProvider.GetServices<IInterceptor>().ToArray();
            if (moduleInterceptors.Length > 0)
            {
                options.AddInterceptors(moduleInterceptors);
            }
        });
        services.AddScoped<IDbInitializer, EFCoreDbInitializer<TDbContext>>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<TDbContext>>();

        // 主 DbContext：注册为基类 DbContext，供 EfCoreEventStore 等基础设施组件使用
        // DbContextRegistrar 也会做同样的事（自动发现模式），TryAdd 防止重复注册
        if (isPrimary)
        {
            services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());
        }



        // 使用 RepositoryRegistrar 注册 Repository
        var registrar = new Services.RepositoryRegistrar();
        registrar.RegisterRepositoriesFromDbContext(services, typeof(TDbContext));
        registrar.RegisterRepositoriesFromEntityRegisters(services, typeof(TDbContext), isPrimary);

        // 注册单据连续编号服务(框架级通用原语:发票号/支票号等法定连续号)。
        // 挂在 DbContext 注册漏斗内(而非 EFCoreModule.ConfigureServices):它依赖
        // IRepository<DocumentSequence>,仅当应用配置了 DbContext 时该仓储才存在——
        // 无数据库的轻宿主(如 HostingLite)加载 EFCoreModule 时无条件注册会在
        // ValidateOnBuild 阶段因仓储缺失炸掉宿主。TryAdd 兼容多 DbContext 与自定义替换。
        services.TryAddScoped<IDocumentNumberService, DocumentNumberService>();

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
