
namespace Tnzi.Utilities;

/// <summary>
/// 快速反射调用处理器，使用表达式树缓存提高反射性能
/// </summary>
public static class FastInvokeHandler
{
    private static readonly ConcurrentDictionary<MethodInfo, Delegate> MethodCache = new();
    private static readonly ConcurrentDictionary<PropertyInfo, Func<object, object?>> PropertyGetterCache = new();
    private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>> PropertySetterCache = new();
    private static readonly ConcurrentDictionary<FieldInfo, Func<object, object?>> FieldGetterCache = new();
    private static readonly ConcurrentDictionary<FieldInfo, Action<object, object?>> FieldSetterCache = new();

    #region 方法调用

    /// <summary>
    /// 快速调用实例方法
    /// </summary>
    /// <param name="instance">实例对象</param>
    /// <param name="method">方法信息</param>
    /// <param name="parameters">方法参数</param>
    /// <returns>方法返回值</returns>
    public static object? FastInvoke(object instance, MethodInfo method, params object?[]? parameters)
    {
        Check.NotNull(instance);
        Check.NotNull(method);

        var delegateCache = MethodCache.GetOrAdd(method, CreateMethodDelegate);
        return delegateCache.DynamicInvoke(parameters);
    }

    /// <summary>
    /// 快速调用泛型实例方法
    /// </summary>
    /// <typeparam name="T">实例类型</typeparam>
    /// <param name="instance">实例对象</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="parameters">方法参数</param>
    /// <returns>方法返回值</returns>
    public static object? FastInvoke<T>(T instance, string methodName, params object?[]? parameters)
    {
        Check.NotNull(instance);
        Check.NotNullOrEmpty(methodName);

        var method = typeof(T).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException($"Method '{methodName}' not found on type '{typeof(T).Name}'.");

        return FastInvoke(instance!, method, parameters);
    }

    /// <summary>
    /// 快速调用静态方法
    /// </summary>
    /// <param name="method">方法信息</param>
    /// <param name="parameters">方法参数</param>
    /// <returns>方法返回值</returns>
    public static object? FastInvokeStatic(MethodInfo method, params object?[]? parameters)
    {
        Check.NotNull(method);
        Check.Condition(method.IsStatic, "Method must be static.");

        var delegateCache = MethodCache.GetOrAdd(method, CreateMethodDelegate);
        return delegateCache.DynamicInvoke(parameters);
    }

    #endregion

    #region 属性访问

    /// <summary>
    /// 快速获取属性值
    /// </summary>
    /// <param name="instance">实例对象</param>
    /// <param name="property">属性信息</param>
    /// <returns>属性值</returns>
    public static object? FastGetProperty(object instance, PropertyInfo property)
    {
        Check.NotNull(instance);
        Check.NotNull(property);
        if (!property.CanRead)
            throw new InvalidOperationException($"Property '{property.Name}' is not readable.");

        var getter = PropertyGetterCache.GetOrAdd(property, CreatePropertyGetter);
        return getter(instance);
    }

    /// <summary>
    /// 快速设置属性值
    /// </summary>
    /// <param name="instance">实例对象</param>
    /// <param name="property">属性信息</param>
    /// <param name="value">属性值</param>
    public static void FastSetProperty(object instance, PropertyInfo property, object? value)
    {
        Check.NotNull(instance);
        Check.NotNull(property);
        if (!property.CanWrite)
            throw new InvalidOperationException($"Property '{property.Name}' is not writable.");

        var setter = PropertySetterCache.GetOrAdd(property, CreatePropertySetter);
        setter(instance, value);
    }

    /// <summary>
    /// 快速获取属性值（泛型）
    /// </summary>
    /// <typeparam name="T">实例类型</typeparam>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="instance">实例对象</param>
    /// <param name="propertyName">属性名称</param>
    /// <returns>属性值</returns>
    public static TProperty? FastGetProperty<T, TProperty>(T instance, string propertyName)
    {
        Check.NotNull(instance);
        Check.NotNullOrEmpty(propertyName);

        var property = typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        if (property == null)
            throw new InvalidOperationException($"Property '{propertyName}' not found on type '{typeof(T).Name}'.");

        var value = FastGetProperty(instance!, property);
        return value is TProperty propertyValue ? propertyValue : default;
    }

    /// <summary>
    /// 快速设置属性值（泛型）
    /// </summary>
    /// <typeparam name="T">实例类型</typeparam>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="instance">实例对象</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="value">属性值</param>
    public static void FastSetProperty<T, TProperty>(T instance, string propertyName, TProperty? value)
    {
        Check.NotNull(instance);
        Check.NotNullOrEmpty(propertyName);

        var property = typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        if (property == null)
            throw new InvalidOperationException($"Property '{propertyName}' not found on type '{typeof(T).Name}'.");

        FastSetProperty(instance!, property, value);
    }

    #endregion

    #region 字段访问

    /// <summary>
    /// 快速获取字段值
    /// </summary>
    /// <param name="instance">实例对象</param>
    /// <param name="field">字段信息</param>
    /// <returns>字段值</returns>
    public static object? FastGetField(object instance, FieldInfo field)
    {
        Check.NotNull(instance);
        Check.NotNull(field);

        var getter = FieldGetterCache.GetOrAdd(field, CreateFieldGetter);
        return getter(instance);
    }

    /// <summary>
    /// 快速设置字段值
    /// </summary>
    /// <param name="instance">实例对象</param>
    /// <param name="field">字段信息</param>
    /// <param name="value">字段值</param>
    public static void FastSetField(object instance, FieldInfo field, object? value)
    {
        Check.NotNull(instance);
        Check.NotNull(field);

        var setter = FieldSetterCache.GetOrAdd(field, CreateFieldSetter);
        setter(instance, value);
    }

    #endregion

    #region 私有方法 - 创建委托

    private static Delegate CreateMethodDelegate(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parameterExpressions = new ParameterExpression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameterExpressions[i] = Expression.Parameter(typeof(object), $"p{i}");
        }

        var instanceParameter = Expression.Parameter(typeof(object), "instance");
        var instanceCast = method.IsStatic ? null : Expression.Convert(instanceParameter, method.DeclaringType!);
        var parameterCasts = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameterCasts[i] = Expression.Convert(parameterExpressions[i], parameters[i].ParameterType);
        }

        var methodCall = method.IsStatic
            ? Expression.Call(method, parameterCasts)
            : Expression.Call(instanceCast!, method, parameterCasts);

        var returnType = method.ReturnType;
        Expression body = returnType == typeof(void)
            ? (Expression)methodCall
            : Expression.Convert(methodCall, typeof(object));

        var lambdaParameters = method.IsStatic
            ? parameterExpressions
            : new[] { instanceParameter }.Concat(parameterExpressions).ToArray();

        var lambda = Expression.Lambda(body, lambdaParameters);
        return lambda.Compile();
    }

    private static Func<object, object?> CreatePropertyGetter(PropertyInfo property)
    {
        var instanceParameter = Expression.Parameter(typeof(object), "instance");
        var instanceCast = Expression.Convert(instanceParameter, property.DeclaringType!);
        var propertyAccess = Expression.Property(instanceCast, property);
        var convert = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<object, object?>>(convert, instanceParameter);
        return lambda.Compile();
    }

    private static Action<object, object?> CreatePropertySetter(PropertyInfo property)
    {
        var instanceParameter = Expression.Parameter(typeof(object), "instance");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var instanceCast = Expression.Convert(instanceParameter, property.DeclaringType!);
        var valueCast = Expression.Convert(valueParameter, property.PropertyType);
        var propertyAccess = Expression.Property(instanceCast, property);
        var assign = Expression.Assign(propertyAccess, valueCast);
        var lambda = Expression.Lambda<Action<object, object?>>(assign, instanceParameter, valueParameter);
        return lambda.Compile();
    }

    private static Func<object, object?> CreateFieldGetter(FieldInfo field)
    {
        var instanceParameter = Expression.Parameter(typeof(object), "instance");
        var instanceCast = Expression.Convert(instanceParameter, field.DeclaringType!);
        var fieldAccess = field.IsStatic
            ? Expression.Field(null, field)
            : Expression.Field(instanceCast, field);
        var convert = Expression.Convert(fieldAccess, typeof(object));
        var lambda = Expression.Lambda<Func<object, object?>>(convert, instanceParameter);
        return lambda.Compile();
    }

    private static Action<object, object?> CreateFieldSetter(FieldInfo field)
    {
        var instanceParameter = Expression.Parameter(typeof(object), "instance");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var instanceCast = field.IsStatic ? null : Expression.Convert(instanceParameter, field.DeclaringType!);
        var valueCast = Expression.Convert(valueParameter, field.FieldType);
        var fieldAccess = field.IsStatic
            ? Expression.Field(null, field)
            : Expression.Field(instanceCast!, field);
        var assign = Expression.Assign(fieldAccess, valueCast);
        var lambdaParameters = field.IsStatic
            ? new[] { valueParameter }
            : new[] { instanceParameter, valueParameter };
        var lambda = Expression.Lambda<Action<object, object?>>(assign, lambdaParameters);
        return lambda.Compile();
    }

    #endregion

    #region 缓存管理

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public static void ClearCache()
    {
        MethodCache.Clear();
        PropertyGetterCache.Clear();
        PropertySetterCache.Clear();
        FieldGetterCache.Clear();
        FieldSetterCache.Clear();
    }

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    /// <returns>缓存统计信息</returns>
    public static (int Methods, int PropertyGetters, int PropertySetters, int FieldGetters, int FieldSetters) GetCacheStats()
    {
        return (
            MethodCache.Count,
            PropertyGetterCache.Count,
            PropertySetterCache.Count,
            FieldGetterCache.Count,
            FieldSetterCache.Count
        );
    }

    #endregion
}