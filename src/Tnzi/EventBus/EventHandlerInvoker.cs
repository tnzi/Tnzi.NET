
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
    /// 本次派发选中的 IEventHandler&lt;TEvent&gt; 接口类型
    /// （处理器实现多个事件接口时，这里是接住当前事件类型的那一个）
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
/// <remarks>
/// <para>
/// <b>缓存键必须是（处理器类型, 事件类型）二元组，不能只是处理器类型。</b>
/// 一个类实现 <c>IEventHandler&lt;A&gt;</c> 与 <c>IEventHandler&lt;B&gt;</c> 是受支持的写法
/// （框架内与应用内都有），而按处理器类型单键缓存只能编译出一个委托，绑死在
/// <c>GetInterfaces()</c> 恰好先返回的那个接口上；派发另一个事件时
/// <c>Expression.Convert</c> 转换到错误的事件类型，抛 <c>InvalidCastException</c>。
/// 由于总线做错误隔离，症状不是崩溃而是<b>那个处理方法从未执行</b>，且每次派发都白烧一轮重试与死信。
/// </para>
/// <para>
/// 键扩到二元组并不改变"编译一次"这个存在理由：编译仍然只发生在每个
/// （处理器, 事件）组合第一次派发时，不是每次派发。二元组才是委托的正确粒度 ——
/// 一个委托本来就只对一个事件类型成立。
/// </para>
/// </remarks>
public static class EventHandlerInvoker
{
    private static readonly ConcurrentDictionary<(Type HandlerType, Type EventType), HandlerMetadata> _metadataCache = new();
    private static readonly ConcurrentDictionary<Type, List<Type>> _baseHandlerInterfaceCache = new();

    /// <summary>
    /// 获取或创建处理器元数据（线程安全，每个「处理器类型 + 事件类型」组合只编译一次）
    /// </summary>
    /// <param name="handlerType">处理器的具体运行时类型</param>
    /// <param name="eventType">本次派发的事件类型（决定选中处理器的哪一个事件接口）</param>
    public static HandlerMetadata GetMetadata(Type handlerType, Type eventType)
    {
        Check.NotNull(handlerType);
        Check.NotNull(eventType);

        return _metadataCache.GetOrAdd(
            (handlerType, eventType),
            static key => BuildMetadata(key.HandlerType, key.EventType));
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
                    Debug.WriteLine($"Failed to create generic handler interface for base type '{baseType.Name}': {ex.Message}");
                }
                baseType = baseType.BaseType;
            }
            return interfaces;
        });
    }

    private static HandlerMetadata BuildMetadata(Type handlerType, Type eventType)
    {
        var metadata = new HandlerMetadata();

        // 检查是否标记为后台执行（按处理器类型，与事件无关）
        metadata.IsBackground = handlerType.GetCustomAttribute<BackgroundEventHandlerAttribute>() != null;

        // 查找能接住本次事件的 IConditionalEventHandler<TEvent>。
        // 只对该事件有条件接口时才建 CanHandle 委托：处理器可能对 A 有条件、对 B 无条件，
        // 拿 A 的 CanHandle 去闸 B 既会转型失败，也不是它声明的语义。
        var conditionalInterface = SelectHandlerInterface(handlerType, eventType, typeof(IConditionalEventHandler<>));
        if (conditionalInterface != null)
        {
            var eventParamType = conditionalInterface.GetGenericArguments()[0];
            var canHandleMethod = conditionalInterface.GetMethod("CanHandle", new[] { eventParamType });
            if (canHandleMethod != null)
            {
                metadata.CanHandleDelegate = BuildCanHandleDelegate(handlerType, canHandleMethod, eventParamType);
            }
        }

        // 查找能接住本次事件的 IEventHandler<TEvent>
        var handlerInterface = SelectHandlerInterface(handlerType, eventType, typeof(IEventHandler<>));
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
    /// 在处理器实现的一组 <paramref name="openHandlerInterface"/> 闭合接口里，
    /// 挑出应当接住 <paramref name="eventType"/> 的那一个。
    /// </summary>
    /// <remarks>
    /// <para>选择规则，按优先级：</para>
    /// <list type="number">
    /// <item>只考虑<b>接得住</b>该事件的接口（事件参数类型可由事件类型赋值）——
    /// 这一条同时覆盖精确匹配与事件继承：注册在基类事件上的处理器要能收到派生事件，
    /// 而 <c>IEventHandler&lt;in TEvent&gt;</c> 的逆变正是为此。</item>
    /// <item>候选多于一个时取<b>最具体</b>的：同时实现基类与派生类接口时，
    /// 派发派生事件应落到派生重载，否则等于悄悄降级去跑基类逻辑。</item>
    /// <item>仍然并列（只可能出现在互不相关的接口型事件契约上）时，
    /// 按类型全名定序。这里没有"正确答案"，但至少要<b>可复现</b>：
    /// 依赖 <c>GetInterfaces()</c> 的返回顺序会让同一份代码在不同运行时/不同编译下选中不同重载。</item>
    /// </list>
    /// <para>
    /// 一个都接不住时返回 <see langword="null"/>，由调用方（各总线）记录告警。
    /// 正常路径下不会发生：处理器是按 <c>IEventHandler&lt;事件类型&gt;</c> 从容器里解析出来的。
    /// </para>
    /// </remarks>
    private static Type? SelectHandlerInterface(Type handlerType, Type eventType, Type openHandlerInterface)
    {
        Type? best = null;
        Type? bestEventParamType = null;

        foreach (var candidate in handlerType.GetInterfaces())
        {
            if (!candidate.IsGenericType || candidate.GetGenericTypeDefinition() != openHandlerInterface)
                continue;

            var eventParamType = candidate.GetGenericArguments()[0];

            // 接不住这个事件：不是本次派发的目标重载
            if (!eventParamType.IsAssignableFrom(eventType))
                continue;

            if (best == null)
            {
                best = candidate;
                bestEventParamType = eventParamType;
                continue;
            }

            // 候选更具体（当前最优能由候选赋值 ⇒ 候选是更派生的那个）
            if (bestEventParamType!.IsAssignableFrom(eventParamType))
            {
                best = candidate;
                bestEventParamType = eventParamType;
                continue;
            }

            // 当前最优更具体：保留
            if (eventParamType.IsAssignableFrom(bestEventParamType))
                continue;

            // 互不可赋值：按全名定序，保证同一份代码每次都选中同一个重载
            if (string.CompareOrdinal(eventParamType.FullName, bestEventParamType.FullName) < 0)
            {
                best = candidate;
                bestEventParamType = eventParamType;
            }
        }

        return best;
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
