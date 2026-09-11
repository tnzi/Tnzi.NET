
namespace Tnzi.EFCore.Internal;

/// <summary>
/// DbContext 服务解析工具类
/// 提供从 DbContext 获取 DI 服务的统一方法
/// </summary>
internal static class DbContextServiceResolver
{
    /// <summary>
    /// 从 DbContext 获取**应用**的 <see cref="IServiceProvider"/>
    /// </summary>
    /// <remarks>
    /// ★ 必须取应用容器，不能取 EF 内部容器。<c>dbContext.GetService&lt;IServiceProvider&gt;()</c>
    /// 命中的恰恰是 EF 内部容器自己（依赖注入容器一律把 <see cref="IServiceProvider"/> 解析为当前作用域），
    /// 应用注册的服务在那里一个都看不到，而且它**有值**——于是调用方拿到一个能用但答什么都没有的容器。
    /// 症状是 <c>TnziDbContextHelper</c> 派发领域事件时取不到 <c>IEventBus</c>，事件被静默丢弃：
    /// 没有异常、没有日志、没有事件。
    /// 应用容器由 <c>AddDbContext</c> 记在 <see cref="CoreOptionsExtension.ApplicationServiceProvider"/>
    /// 上（框架用的是 <c>(serviceProvider, options)</c> 重载，因此它是请求作用域容器，
    /// Scoped 服务可正常解析）。
    /// </remarks>
    public static IServiceProvider? GetServiceProvider(DbContext dbContext)
    {
        try
        {
            var applicationServiceProvider = dbContext
                .GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()
                ?.ApplicationServiceProvider;

            if (applicationServiceProvider != null)
            {
                return applicationServiceProvider;
            }
        }
        catch
        {
            // 选项扩展不可得（手工 new 出来的 DbContext）时走下面的回退
        }

        // 回退：EF 内部容器。手工构造的 DbContext 没有应用容器，此时它是唯一可得的容器，
        // 解析应用服务会全部落空，但这与「没有容器」是同一个结果，不会更糟。
        try
        {
            return dbContext is IInfrastructure<IServiceProvider> infra ? infra.Instance : null;
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
