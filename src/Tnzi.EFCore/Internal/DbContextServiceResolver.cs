
namespace Tnzi.EFCore.Internal;

/// <summary>
/// DbContext 服务解析工具类
/// 提供从 DbContext 获取 DI 服务的统一方法
/// </summary>
internal static class DbContextServiceResolver
{
    /// <summary>
    /// 从 DbContext 获取 IServiceProvider
    /// </summary>
    public static IServiceProvider? GetServiceProvider(DbContext dbContext)
    {
        try
        {
            var provider = dbContext.Database.GetService<IServiceProvider>();
            if (provider == null && dbContext is IInfrastructure<IServiceProvider> infra)
            {
                provider = infra.Instance;
            }
            return provider;
        }
        catch { return null; }
    }

    /// <summary>
    /// 从 DbContext 获取 Logger
    /// </summary>
    public static ILogger? GetLogger(DbContext dbContext)
    {
        try
        {
            var factory = dbContext.Database.GetService<ILoggerFactory>();
            return factory?.CreateLogger(dbContext.GetType());
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取文件引用处理器
    /// </summary>
    public static IFileReferenceProcessor? GetFileReferenceProcessor(DbContext dbContext)
    {
        // 优先从应用 DI 容器获取（IFileReferenceProcessor 注册在应用 DI 中）
        var serviceProvider = GetServiceProvider(dbContext);
        if (serviceProvider != null)
        {
            try
            {
                var processor = serviceProvider.GetService<IFileReferenceProcessor>();
                if (processor != null) return processor;
            }
            catch
            {
                // 服务可能未注册，这是预期情况，继续尝试其他方式
            }
        }

        // 回退到 EF Core 内部 DI（通常不会成功，但保留兼容性）
        try { return dbContext.Database.GetService<IFileReferenceProcessor>(); }
        catch
        {
            // 服务可能未注册，返回 null 表示未找到
            return null;
        }
    }
}
