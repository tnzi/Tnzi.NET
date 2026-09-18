
namespace Tnzi.Data.Filtering;

/// <summary>
/// 过滤表达式构建器 - 将 FilterGroup 转换为 Expression&lt;Func&lt;T, bool&gt;&gt;
/// 采用静态方法设计，无状态，线程安全
/// </summary>
public static class FilterExpressionBuilder
{
    // 操作符与表达式工厂的映射（缓存以提升性能）
    private static readonly Dictionary<FilterOperator, Func<Expression, Expression, Expression>> OperatorMap = new()
    {
        { FilterOperator.Equal, Expression.Equal },
        { FilterOperator.NotEqual, Expression.NotEqual },
        { FilterOperator.GreaterThan, Expression.GreaterThan },
        { FilterOperator.GreaterThanOrEqual, Expression.GreaterThanOrEqual },
        { FilterOperator.LessThan, Expression.LessThan },
        { FilterOperator.LessThanOrEqual, Expression.LessThanOrEqual },
        { FilterOperator.Contains, BuildContainsExpression },
        { FilterOperator.NotContains, BuildNotContainsExpression },
        { FilterOperator.StartsWith, BuildStartsWithExpression },
        { FilterOperator.EndsWith, BuildEndsWithExpression },
        { FilterOperator.In, BuildInExpression },
        { FilterOperator.NotIn, BuildNotInExpression },
        { FilterOperator.IsNull, (left, _) => Expression.Equal(left, Expression.Constant(null, left.Type)) },
        { FilterOperator.IsNotNull, (left, _) => Expression.NotEqual(left, Expression.Constant(null, left.Type)) },
    };

    /// <summary>
    /// 从 FilterGroup 构建表达式（不限制字段：给服务端自己构造的过滤器用）。
    /// 请求来源的过滤器走 <see cref="Build{T}(FilterGroup?, FilterFieldPolicy)"/> 并传 <see cref="FilterFieldPolicy.Request"/>。
    /// </summary>
    public static Expression<Func<T, bool>> Build<T>(FilterGroup? group)
    {
        return Build<T>(group, FilterFieldPolicy.Unrestricted);
    }

    /// <summary>
    /// 从 FilterGroup 构建表达式，字段先经 <paramref name="policy"/> 准入；不允许的字段抛 <see cref="FilterFieldException"/>（400）
    /// </summary>
    public static Expression<Func<T, bool>> Build<T>(FilterGroup? group, FilterFieldPolicy policy)
    {
        Check.NotNull(policy);
        var param = Expression.Parameter(typeof(T), "e");

        if (group == null || !group.HasFilters)
        {
            // 无过滤条件，返回 true
            return Expression.Lambda<Func<T, bool>>(Expression.Constant(true), param);
        }

        EnsureFieldsAllowed(typeof(T), group, policy);
        var body = BuildGroupExpression(param, group);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// 校验 <paramref name="group"/> 里每条规则（含嵌套分组）的字段是否被 <paramref name="policy"/> 允许；
    /// 失败时 <paramref name="error"/> 是可直接回给调用方的消息（不含实体类型名）
    /// </summary>
    public static bool TryValidateFields<T>(FilterGroup? group, FilterFieldPolicy policy, out string? error)
    {
        Check.NotNull(policy);
        error = null;
        if (group == null || !group.HasFilters)
            return true;

        var rejected = FindRejectedField(typeof(T), group, policy);
        if (rejected == null)
            return true;

        error = new FilterFieldException(rejected).Message;
        return false;
    }

    /// <summary>
    /// 校验 <see cref="PagedQuery.OrderBy"/>（<c>"Name DESC, Id"</c> 形态）里每个字段是否被 <paramref name="policy"/> 允许。
    /// 排序是过滤的弱形态：按一条从不投影的列排序就把它的相对次序交了出去，所以过同一道门。
    /// 失败时 <paramref name="error"/> 只含字段名，不含实体类型名。
    /// </summary>
    public static bool TryValidateOrderBy<T>(string? orderBy, FilterFieldPolicy policy, out string? error)
    {
        Check.NotNull(policy);
        error = null;
        if (string.IsNullOrWhiteSpace(orderBy))
            return true;

        foreach (var part in orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var field = part.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (policy.IsAllowed(typeof(T), field))
                continue;

            error = $"Sort field '{field}' is not sortable.";
            return false;
        }

        return true;
    }

    private static void EnsureFieldsAllowed(Type rootType, FilterGroup group, FilterFieldPolicy policy)
    {
        var rejected = FindRejectedField(rootType, group, policy);
        if (rejected != null)
            throw new FilterFieldException(rejected);
    }

    private static string? FindRejectedField(Type rootType, FilterGroup group, FilterFieldPolicy policy)
    {
        foreach (var rule in group.Rules)
        {
            if (!policy.IsAllowed(rootType, rule.Field))
                return rule.Field;
        }

        foreach (var subGroup in group.Groups)
        {
            var rejected = FindRejectedField(rootType, subGroup, policy);
            if (rejected != null)
                return rejected;
        }

        return null;
    }

    /// <summary>
    /// 从单个 FilterRule 构建表达式
    /// </summary>
    public static Expression<Func<T, bool>> Build<T>(FilterRule rule)
    {
        Check.NotNull(rule);
        
        var param = Expression.Parameter(typeof(T), "e");
        var body = BuildRuleExpression(param, rule);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    // Cache for FilterProperty metadata per DTO type
    private static readonly ConcurrentDictionary<Type, FilterPropertyMetadata[]> _dtoMetadataCache = new();

    /// <summary>
    /// Build a filter expression from a DTO instance decorated with [FilterProperty] attributes.
    /// Null or default-valued properties are automatically skipped.
    /// </summary>
    public static Expression<Func<TEntity, bool>> BuildFromDto<TEntity, TDto>(TDto dto, LogicalOperator logic = LogicalOperator.And)
        where TDto : class
    {
        Check.NotNull(dto);

        var metadata = GetFilterPropertyMetadata(typeof(TDto));
        if (metadata.Length == 0)
        {
            var param = Expression.Parameter(typeof(TEntity), "e");
            return Expression.Lambda<Func<TEntity, bool>>(Expression.Constant(true), param);
        }

        var group = new FilterGroup { Logic = logic };

        foreach (var meta in metadata)
        {
            var value = meta.PropertyInfo.GetValue(dto);
            if (value == null) continue;
            if (IsDefaultValue(value, meta.PropertyInfo.PropertyType)) continue;
            if (value is string str && string.IsNullOrWhiteSpace(str)) continue;

            var entityField = meta.EntityProperty ?? meta.PropertyInfo.Name;
            group.AddRule(entityField, meta.Operator, value);
        }

        return Build<TEntity>(group);
    }

    /// <summary>
    /// Build a FilterGroup from a DTO instance decorated with [FilterProperty] attributes.
    /// Useful when you need to further customize the filter before building the expression.
    /// </summary>
    public static FilterGroup BuildGroupFromDto<TDto>(TDto dto, LogicalOperator logic = LogicalOperator.And)
        where TDto : class
    {
        Check.NotNull(dto);

        var metadata = GetFilterPropertyMetadata(typeof(TDto));
        var group = new FilterGroup { Logic = logic };

        foreach (var meta in metadata)
        {
            var value = meta.PropertyInfo.GetValue(dto);
            if (value == null) continue;
            if (IsDefaultValue(value, meta.PropertyInfo.PropertyType)) continue;
            if (value is string str && string.IsNullOrWhiteSpace(str)) continue;

            var entityField = meta.EntityProperty ?? meta.PropertyInfo.Name;
            group.AddRule(entityField, meta.Operator, value);
        }

        return group;
    }

    private static FilterPropertyMetadata[] GetFilterPropertyMetadata(Type dtoType)
    {
        return _dtoMetadataCache.GetOrAdd(dtoType, type =>
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var result = new List<FilterPropertyMetadata>();

            foreach (var prop in properties)
            {
                var attr = prop.GetCustomAttribute<FilterPropertyAttribute>();
                if (attr == null) continue;

                result.Add(new FilterPropertyMetadata(prop, attr.EntityProperty, attr.Operator, attr.Logic));
            }

            return result.ToArray();
        });
    }

    private static bool IsDefaultValue(object value, Type propertyType)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (!underlyingType.IsValueType) return false;
        var defaultValue = Activator.CreateInstance(underlyingType);
        return value.Equals(defaultValue);
    }

    private sealed record FilterPropertyMetadata(
        PropertyInfo PropertyInfo,
        string? EntityProperty,
        FilterOperator Operator,
        LogicalOperator Logic);

    /// <summary>
    /// 验证 FilterGroup 是否能正常转换为表达式
    /// </summary>
    public static bool TryBuild<T>(FilterGroup? group, out Expression<Func<T, bool>>? expression, out string? error)
    {
        try
        {
            expression = Build<T>(group);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            expression = null;
            error = ex.Message;
            return false;
        }
    }

    private static Expression BuildGroupExpression(ParameterExpression param, FilterGroup group)
    {
        var expressions = new List<Expression>();

        // 处理规则
        foreach (var rule in group.Rules)
        {
            expressions.Add(BuildRuleExpression(param, rule));
        }

        // 递归处理子分组
        foreach (var subGroup in group.Groups)
        {
            if (subGroup.HasFilters)
            {
                expressions.Add(BuildGroupExpression(param, subGroup));
            }
        }

        if (expressions.Count == 0)
        {
            return Expression.Constant(true);
        }

        // 根据逻辑运算符合并表达式
        return group.Logic == LogicalOperator.And
            ? expressions.Aggregate(Expression.AndAlso)
            : expressions.Aggregate(Expression.OrElse);
    }

    private static Expression BuildRuleExpression(ParameterExpression param, FilterRule rule)
    {
        // 解析嵌套属性（如 "Author.Name"）：字段不存在答「不可过滤」，与准入策略同一句
        var propertyAccess = BuildPropertyAccess(param, rule.Field);

        // 字段之后的一切失败（操作符不认识、值解析不了、类型不匹配）都是请求给的值有问题：
        // 一律 400 且只报字段与操作符，CLR 类型名留在内部异常里进日志
        try
        {
            return BuildOperatorExpression(propertyAccess, rule);
        }
        catch (Exception ex) when (ex is not TnziException)
        {
            throw new FilterValueException(rule.Field, rule.Operator, ex);
        }
    }

    private static Expression BuildOperatorExpression(Expression propertyAccess, FilterRule rule)
    {
        // 获取操作符对应的表达式工厂
        if (!OperatorMap.TryGetValue(rule.Operator, out var factory))
        {
            throw new NotSupportedException($"Operator '{rule.Operator}' is not supported.");
        }

        // IsNull 和 IsNotNull 不需要转换值
        if (rule.Operator == FilterOperator.IsNull || rule.Operator == FilterOperator.IsNotNull)
        {
            return factory(propertyAccess, Expression.Constant(null));
        }

        // In 和 NotIn 需要特殊处理数组
        if (rule.Operator == FilterOperator.In || rule.Operator == FilterOperator.NotIn)
        {
            var arrayConstant = BuildArrayConstant(rule.Value, propertyAccess.Type);
            return factory(propertyAccess, arrayConstant);
        }

        // 字符串专用操作符需要先检查属性类型（避免类型转换先于类型检查）
        if (rule.Operator is FilterOperator.Contains or FilterOperator.NotContains
            or FilterOperator.StartsWith or FilterOperator.EndsWith)
        {
            EnsureStringType(propertyAccess, rule.Operator.ToString());
        }

        // 类型转换
        var value = ConvertValue(rule.Value, propertyAccess.Type);
        var constant = Expression.Constant(value, propertyAccess.Type);

        // 构建表达式
        return factory(propertyAccess, constant);
    }

    private static Expression BuildPropertyAccess(Expression source, string propertyPath)
    {
        Check.NotNullOrWhiteSpace(propertyPath);

        var properties = propertyPath.Split('.');
        var result = source;

        foreach (var propName in properties)
        {
            var property = result.Type.GetProperty(propName,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            if (property == null)
            {
                // 400 而不是 500，且消息不带实体类型名：这条异常会原样到达请求方
                throw new FilterFieldException(propertyPath);
            }

            result = Expression.Property(result, property);
        }

        return result;
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
        {
            // 对于值类型，null 无法赋值，抛异常
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
            {
                throw new InvalidOperationException(
                    $"Cannot convert null to non-nullable type '{targetType.Name}'.");
            }
            return null;
        }

        // 处理 System.Text.Json 反序列化产生的 JsonElement 类型
        if (value is JsonElement jsonElement)
        {
            value = ConvertJsonElement(jsonElement, targetType);
            if (value == null)
            {
                return null;
            }
            // 如果转换后类型已匹配，直接返回
            if (value.GetType() == (Nullable.GetUnderlyingType(targetType) ?? targetType))
            {
                return value;
            }
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value.GetType() == underlyingType)
        {
            return value;
        }

        // 特殊处理枚举
        if (underlyingType.IsEnum)
        {
            if (value is string strValue)
            {
                return Enum.Parse(underlyingType, strValue, ignoreCase: true);
            }
            if (value is int intValue)
            {
                return Enum.ToObject(underlyingType, intValue);
            }
        }

        // 特殊处理 Guid
        if (underlyingType == typeof(Guid) && value is string guidStr)
        {
            return Guid.Parse(guidStr);
        }

        // 特殊处理 DateTime
        if (underlyingType == typeof(DateTime) && value is string dateStr)
        {
            return DateTime.Parse(dateStr);
        }

        // 特殊处理 DateTimeOffset
        if (underlyingType == typeof(DateTimeOffset) && value is string offsetStr)
        {
            return DateTimeOffset.Parse(offsetStr);
        }

        // 特殊处理 TimeSpan
        if (underlyingType == typeof(TimeSpan) && value is string timeStr)
        {
            return TimeSpan.Parse(timeStr);
        }

        return Convert.ChangeType(value, underlyingType);
    }

    private static Expression BuildArrayConstant(object? value, Type elementType)
    {
        Check.NotNull(value);

        var enumerable = value as System.Collections.IEnumerable;
        if (enumerable == null)
        {
            throw new InvalidOperationException("Value for In/NotIn operator must be a collection.");
        }

        var list = new List<object?>();
        foreach (var item in enumerable)
        {
            list.Add(ConvertValue(item, elementType));
        }

        var arrayType = elementType.MakeArrayType();
        var array = Array.CreateInstance(elementType, list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            array.SetValue(list[i], i);
        }

        return Expression.Constant(array, arrayType);
    }

    // 字符串方法表达式构建
    private static Expression BuildContainsExpression(Expression left, Expression right)
    {
        EnsureStringType(left, "Contains");
        var method = typeof(string).GetMethod("Contains", [typeof(string)])!;
        return Expression.Call(left, method, right);
    }

    private static Expression BuildNotContainsExpression(Expression left, Expression right)
        => Expression.Not(BuildContainsExpression(left, right));

    private static Expression BuildStartsWithExpression(Expression left, Expression right)
    {
        EnsureStringType(left, "StartsWith");
        var method = typeof(string).GetMethod("StartsWith", [typeof(string)])!;
        return Expression.Call(left, method, right);
    }

    private static Expression BuildEndsWithExpression(Expression left, Expression right)
    {
        EnsureStringType(left, "EndsWith");
        var method = typeof(string).GetMethod("EndsWith", [typeof(string)])!;
        return Expression.Call(left, method, right);
    }

    private static Expression BuildInExpression(Expression left, Expression right)
    {
        // 使用 Contains 方法（集合.Contains(属性)）
        // right 是数组，left 是要搜索的元素
        var elementType = left.Type;
        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
            .MakeGenericMethod(elementType);
        return Expression.Call(null, containsMethod, right, left);
    }

    private static Expression BuildNotInExpression(Expression left, Expression right)
        => Expression.Not(BuildInExpression(left, right));

    private static void EnsureStringType(Expression expression, string operatorName)
    {
        if (expression.Type != typeof(string))
        {
            throw new NotSupportedException(
                $"'{operatorName}' operator only supports string type, but got '{expression.Type.Name}'.");
        }
    }

    /// <summary>
    /// 将 JsonElement 转换为目标类型的值
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => ConvertJsonString(element.GetString()!, underlyingType),
            JsonValueKind.Number => ConvertJsonNumber(element, underlyingType),
            JsonValueKind.Array => ConvertJsonArray(element, underlyingType),
            _ => element.GetRawText()
        };
    }

    private static object ConvertJsonString(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(Guid)) return Guid.Parse(value);
        if (targetType == typeof(DateTime)) return DateTime.Parse(value);
        if (targetType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value);
        if (targetType == typeof(TimeSpan)) return TimeSpan.Parse(value);
        if (targetType.IsEnum) return Enum.Parse(targetType, value, ignoreCase: true);
        return value;
    }

    private static object ConvertJsonNumber(JsonElement element, Type targetType)
    {
        if (targetType == typeof(int) || targetType == typeof(int?)) return element.GetInt32();
        if (targetType == typeof(long) || targetType == typeof(long?)) return element.GetInt64();
        if (targetType == typeof(double) || targetType == typeof(double?)) return element.GetDouble();
        if (targetType == typeof(decimal) || targetType == typeof(decimal?)) return element.GetDecimal();
        if (targetType == typeof(float) || targetType == typeof(float?)) return element.GetSingle();
        if (targetType == typeof(short) || targetType == typeof(short?)) return element.GetInt16();
        if (targetType == typeof(byte) || targetType == typeof(byte?)) return element.GetByte();
        if (targetType.IsEnum) return Enum.ToObject(targetType, element.GetInt32());
        return element.GetDouble();
    }

    private static object ConvertJsonArray(JsonElement element, Type targetType)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number => item.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => item.GetRawText()
            });
        }
        return list.ToArray();
    }
}