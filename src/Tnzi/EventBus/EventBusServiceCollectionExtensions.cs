
namespace Tnzi.EventBus;

/// <summary>
/// 事件总线服务集合扩展方法
/// 提供配置时移除/替换处理器的便捷方法
/// </summary>
public static class EventBusServiceCollectionExtensions
{
    /// <summary>
    /// 注册事件处理器（自动推断事件类型）
    /// 从处理器类型中自动推断出它处理的<b>每一个</b>事件类型
    /// </summary>
    /// <typeparam name="THandler">处理器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddEventHandler<THandler>(this IServiceCollection services)
        where THandler : class
    {
        return services.AddEventHandler<THandler>(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// 注册事件处理器（显式指定事件类型，更简洁的API）
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddScoped<IEventHandler<TEvent>, THandler>();
        return services;
    }

    /// <summary>
    /// 注册事件处理器（指定生命周期，自动推断事件类型）
    /// </summary>
    /// <typeparam name="THandler">处理器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    /// <remarks>
    /// 处理器声明的<b>每一个</b> <c>IEventHandler&lt;TEvent&gt;</c> 都会被注册。
    /// 早先这里只取 <c>GetInterfaces()</c> 返回的第一个接口，于是一个同时处理两种事件的处理器
    /// 只订阅到其中一种，另一种<b>连派发环节都到不了</b> —— 没有异常、没有日志，
    /// 只是那个事件永远没有处理器。
    /// </remarks>
    public static IServiceCollection AddEventHandler<THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime)
        where THandler : class
    {
        var handlerType = typeof(THandler);

        // 查找处理器实现的全部 IEventHandler<> 接口
        var eventHandlerInterfaces = handlerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
            .ToList();

        if (eventHandlerInterfaces.Count == 0)
        {
            throw new InvalidOperationException(
                $"Handler type {handlerType.Name} does not implement IEventHandler<TEvent> interface.");
        }

        foreach (var eventHandlerInterface in eventHandlerInterfaces)
        {
            // IEventHandler<TEvent> 本身就是要注册的服务类型
            var descriptor = new ServiceDescriptor(eventHandlerInterface, handlerType, lifetime);
            services.Add(descriptor);
        }

        return services;
    }

    /// <summary>
    /// 注册事件处理器（显式指定事件类型和生命周期）
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime)
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        var descriptor = new ServiceDescriptor(typeof(IEventHandler<TEvent>), typeof(THandler), lifetime);
        services.Add(descriptor);
        return services;
    }
    /// <summary>
    /// 移除指定事件类型的处理器
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection RemoveEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        var descriptor = services.FirstOrDefault(s =>
            s.ServiceType == typeof(IEventHandler<TEvent>) &&
            s.ImplementationType == typeof(THandler));

        if (descriptor != null)
        {
            services.Remove(descriptor);
        }

        return services;
    }

    /// <summary>
    /// 移除指定事件类型的所有处理器
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection RemoveAllEventHandlers<TEvent>(this IServiceCollection services)
        where TEvent : class, IEvent
    {
        var descriptors = services
            .Where(s => s.ServiceType == typeof(IEventHandler<TEvent>))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        return services;
    }

    /// <summary>
    /// 替换指定事件类型的处理器
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <typeparam name="TOldHandler">旧处理器类型</typeparam>
    /// <typeparam name="TNewHandler">新处理器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ReplaceEventHandler<TEvent, TOldHandler, TNewHandler>(
        this IServiceCollection services)
        where TEvent : class, IEvent
        where TOldHandler : class, IEventHandler<TEvent>
        where TNewHandler : class, IEventHandler<TEvent>
    {
        return services
            .RemoveEventHandler<TEvent, TOldHandler>()
            .AddScoped<IEventHandler<TEvent>, TNewHandler>();
    }
}