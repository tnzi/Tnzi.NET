
namespace Tnzi.Mapster.Adapters;

/// <summary>
/// Mapster 映射配置构建器实现
/// 使用强类型 API 替代反射调用，确保与 Mapster 7.x 完全兼容
/// </summary>
internal class MapsterMappingConfigBuilder<TSource, TDestination>
    : IMappingConfigBuilder<TSource, TDestination>
{
    private readonly TypeAdapterSetter<TSource, TDestination> _setter;

    // 缓存属性访问委托，避免每次 AfterMapping 都执行反射
    private static readonly ConcurrentDictionary<(Type, string), (Func<object, object?> Getter, Action<object, object?> Setter)> PropertyAccessorCache = new();

    public MapsterMappingConfigBuilder(TypeAdapterSetter<TSource, TDestination> setter)
    {
        _setter = Check.NotNull(setter);
    }

    public IMappingConfigBuilder<TSource, TDestination> Map<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationMember,
        Expression<Func<TSource, TProperty>> sourceExpression)
    {
        // 直接调用 Mapster 的强类型 Map 方法
        _setter.Map(destinationMember, sourceExpression);
        return this;
    }

    /// <remarks>
    /// This method uses AfterMapping callback which is NOT compatible with ProjectTo (SQL projection).
    /// Use this only for in-memory mapping (MapTo/MapToList).
    /// </remarks>
    public IMappingConfigBuilder<TSource, TDestination> Map<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationMember,
        Expression<Func<TSource, TProperty?>> sourceExpression,
        TProperty defaultValue)
    {
        _setter.Map(destinationMember, sourceExpression);

        // 在内存映射时，如果值为 null 则设置默认值
        var propertyInfo = GetPropertyInfo(destinationMember);
        var (getter, setter) = GetOrCreateAccessors(propertyInfo);

        _setter.AfterMapping((src, dest) =>
        {
            var currentValue = getter(dest!);
            if (currentValue == null || (currentValue is string str && string.IsNullOrEmpty(str)))
            {
                setter(dest!, defaultValue);
            }
        });

        return this;
    }

    public IMappingConfigBuilder<TSource, TDestination> Ignore<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationMember)
    {
        // 将表达式转换为 Mapster 要求的 object 类型
        var converted = Expression.Lambda<Func<TDestination, object>>(
            Expression.Convert(destinationMember.Body, typeof(object)),
            destinationMember.Parameters);
        _setter.Ignore(converted);
        return this;
    }

    public IMappingConfigBuilder<TSource, TDestination> MaxDepth(int depth)
    {
        _setter.MaxDepth(depth);
        return this;
    }

    public IMappingConfigBuilder<TSource, TDestination> AfterMapping(
        Action<TSource, TDestination> afterMapping)
    {
        _setter.AfterMapping(afterMapping);
        return this;
    }

    public IMappingConfigBuilder<TSource, TDestination> BeforeMapping(
        Action<TSource, TDestination> beforeMapping)
    {
        _setter.BeforeMapping(beforeMapping);
        return this;
    }

    /// <summary>
    /// 从表达式树中获取属性信息（支持值类型的 UnaryExpression Convert）
    /// </summary>
    private static PropertyInfo GetPropertyInfo<T, TProp>(Expression<Func<T, TProp>> expression)
    {
        var body = expression.Body;

        // 处理值类型的 Convert 表达式 (如 int -> object 的装箱)
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            body = unary.Operand;

        if (body is MemberExpression memberExpression &&
            memberExpression.Member is PropertyInfo propertyInfo)
        {
            return propertyInfo;
        }
        throw new ArgumentException("Expression must be a property access expression.", nameof(expression));
    }

    /// <summary>
    /// 获取或创建属性的编译访问委托
    /// </summary>
    private static (Func<object, object?> Getter, Action<object, object?> Setter) GetOrCreateAccessors(PropertyInfo propertyInfo)
    {
        var key = (propertyInfo.DeclaringType!, propertyInfo.Name);
        return PropertyAccessorCache.GetOrAdd(key, _ =>
        {
            // 编译 getter
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var castInstance = Expression.Convert(instanceParam, propertyInfo.DeclaringType!);
            var propertyAccess = Expression.Property(castInstance, propertyInfo);
            var castResult = Expression.Convert(propertyAccess, typeof(object));
            var getter = Expression.Lambda<Func<object, object?>>(castResult, instanceParam).Compile();

            // 编译 setter
            var valueParam = Expression.Parameter(typeof(object), "value");
            var castValue = Expression.Convert(valueParam, propertyInfo.PropertyType);
            var assign = Expression.Assign(Expression.Property(castInstance, propertyInfo), castValue);
            var setter = Expression.Lambda<Action<object, object?>>(assign, instanceParam, valueParam).Compile();

            return (getter, setter);
        });
    }
}
