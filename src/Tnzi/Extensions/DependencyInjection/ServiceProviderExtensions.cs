namespace Tnzi.Extensions;

/// <summary>
/// IServiceProvider扩展方法
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// 获取指定类型的日志对象
    /// </summary>
    /// <typeparam name="T">非静态强类型</typeparam>
    /// <param name="provider">服务提供者</param>
    /// <returns>日志对象</returns>
    public static ILogger<T> GetLogger<T>(this IServiceProvider provider)
    {
        Check.NotNull(provider);

        ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();
        return factory.CreateLogger<T>();
    }

    /// <summary>
    /// 获取指定类型的日志对象
    /// </summary>
    /// <param name="provider">服务提供者</param>
    /// <param name="type">指定类型</param>
    /// <returns>日志对象</returns>
    public static ILogger GetLogger(this IServiceProvider provider, Type type)
    {
        Check.NotNull(provider);
        Check.NotNull(type);

        ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();
        return factory.CreateLogger(type);
    }
}
