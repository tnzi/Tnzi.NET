
namespace Tnzi.EventBus;

/// <summary>
/// 事件处理器元数据（缓存编译后的调用委托）
/// </summary>
public class HandlerMetadata
{
    /// <summary>
    /// HandleAsync 编译委托：(handler, event, cancellationToken) => Task
    /// </summary>
    public Func<object, object, CancellationToken, Task>? HandleDelegate { get; set; }

    /// <summary>
    /// CanHandle 编译委托：(handler, event) => bool
    /// </summary>
    public Func<object, object, bool>? CanHandleDelegate { get; set; }

    /// <summary>
    /// 处理器实现的 IEventHandler&lt;TEvent&gt; 接口类型
    /// </summary>
    public Type? HandlerInterface { get; set; }

    /// <summary>
    /// 是否为后台执行处理器（标记了 [BackgroundEventHandler] 特性）
    /// </summary>
    public bool IsBackground { get; set; }
}

/// <summary>
/// 事件处理器调用器
/// 提供处理器元数据缓存和基类事件处理器接口缓存
/// 使用编译委托替代 MethodInfo.Invoke，与 ModuleLoader 的表达式树缓存模式一致
/// </summary>
public static class EventHandlerInvoker
{
    private static readonly ConcurrentDictionary<Type, HandlerMetadata> _metadataCache = new();
    private static readonly ConcurrentDictionary<Type, List<Type>> _baseHandlerInterfaceCache = new();

    /// <summary>
    /// 获取或创建处理器元数据（线程安全，每个处理器类型只编译一次）
    /// </summary>
    public static HandlerMetadata GetMetadata(Type handlerType)
    {
        return _metadataCache.GetOrAdd(handlerType, BuildMetadata);
    }

    /// <summary>
    /// 获取基类事件的处理器接口列表（缓存）
    /// </summary>
    public static List<Type> GetBaseHandlerInterfaces(Type eventType)
    {
        return _baseHandlerInterfaceCache.GetOrAdd(eventType, type =>
        {
            var interfaces = new List<Type>();
            var baseType = type.BaseType;
            while (baseType != null && typeof(IEvent).IsAssignableFrom(baseType))
            {
                try
                {
                    interfaces.Add(typeof(IEventHandler<>).MakeGenericType(baseType));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create generic handler interface for base type '{baseType.Name}': {ex.Message}");
                }
                baseType = baseType.BaseType;
            }
            return interfaces;
        });
    }

    private static HandlerMetadata BuildMetadata(Type handlerType)
    {
        var metadata = new HandlerMetadata();

        // 检查是否标记为后台执行
        metadata.IsBackground = handlerType.GetCustomAttribute<BackgroundEventHandlerAttribute>() != null;

        // 查找 IConditionalEventHandler<TEvent>
        var conditionalInterface = handlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConditionalEventHandler<>));
        if (conditionalInterface != null)
        {
            var eventParamType = conditionalInterface.GetGenericArguments()[0];
            var canHandleMethod = conditionalInterface.GetMethod("CanHandle", new[] { eventParamType });
            if (canHandleMethod != null)
            {
                metadata.CanHandleDelegate = BuildCanHandleDelegate(handlerType, canHandleMethod, eventParamType);
            }
        }

        // 查找 IEventHandler<TEvent>
        var handlerInterface = handlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));
        if (handlerInterface != null)
        {
            metadata.HandlerInterface = handlerInterface;
            var eventParamType = handlerInterface.GetGenericArguments()[0];
            var handleMethod = handlerInterface.GetMethod("HandleAsync", new[] { eventParamType, typeof(CancellationToken) });
            if (handleMethod != null)
            {
                metadata.HandleDelegate = BuildHandleDelegate(handlerType, handleMethod, eventParamType);
            }
        }

        return metadata;
    }

    /// <summary>
    /// 编译 CanHandle 委托：(object handler, object event) => bool
    /// </summary>
    private static Func<object, object, bool> BuildCanHandleDelegate(Type handlerType, MethodInfo method, Type eventParamType)
    {
        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var eventParam = Expression.Parameter(typeof(object), "event");

        var call = Expression.Call(
            Expression.Convert(handlerParam, handlerType),
            method,
            Expression.Convert(eventParam, eventParamType));

        return Expression.Lambda<Func<object, object, bool>>(call, handlerParam, eventParam).Compile();
    }

    /// <summary>
    /// 编译 HandleAsync 委托：(object handler, object event, CancellationToken ct) => Task
    /// </summary>
    private static Func<object, object, CancellationToken, Task> BuildHandleDelegate(Type handlerType, MethodInfo method, Type eventParamType)
    {
        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var eventParam = Expression.Parameter(typeof(object), "event");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var call = Expression.Call(
            Expression.Convert(handlerParam, handlerType),
            method,
            Expression.Convert(eventParam, eventParamType),
            ctParam);

        return Expression.Lambda<Func<object, object, CancellationToken, Task>>(call, handlerParam, eventParam, ctParam).Compile();
    }
}
