// 名字冲突消歧：System.ComponentModel.DataAnnotations 也有一个 ValidationException
using ValidationException = Tnzi.Exceptions.ValidationException;

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
    /// 从 FilterGroup 构建表达式，字段先经 <paramref name="policy"/> 准入；不允许的字段抛 <see cref="FilterFieldException"/>（400），
    /// 规则总数或分组层数超出策略上限抛 <see cref="ValidationException"/>（400）
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

        var shapeViolation = FindShapeViolation(group, policy);
        if (shapeViolation != null)
            throw new ValidationException(shapeViolation);

        EnsureFieldsAllowed(typeof(T), group, policy);
        var body = BuildGroupExpression(param, group);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// 校验 <paramref name="group"/> 里每条规则（含嵌套分组）的字段是否被 <paramref name="policy"/> 允许，
    /// 以及规则总数与分组层数是否在策略上限内；
    /// 失败时 <paramref name="error"/> 是可直接回给调用方的消息（不含实体类型名）
    /// </summary>
    public static bool TryValidateFields<T>(FilterGroup? group, FilterFieldPolicy policy, out string? error)
    {
        Check.NotNull(policy);
        error = null;
        if (group == null || !group.HasFilters)
            return true;

        error = FindShapeViolation(group, policy);
        if (error != null)
            return false;

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

    /// <summary>
    /// 规则总数（含全部嵌套分组）与分组层数是否在 <paramref name="policy"/> 的上限内；超出时返回可直接回给调用方的消息
    /// </summary>
    private static string? FindShapeViolation(FilterGroup group, FilterFieldPolicy policy)
    {
        var ruleCount = 0;
        return Visit(group, 1);

        string? Visit(FilterGroup current, int depth)
        {
            if (depth > policy.MaxGroupDepth)
                return $"Filter groups are nested too deeply; at most {policy.MaxGroupDepth} levels are allowed.";

            ruleCount += current.Rules.Count;
            if (ruleCount > policy.MaxRules)
                return $"Filter has too many rules; at most {policy.MaxRules} are allowed.";

            foreach (var subGroup in current.Groups)
            {
                var violation = Visit(subGroup, depth + 1);
                if (violation != null)
                    return violation;
            }

            return null;
        }
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
    /// Properties that are not set are skipped: null, blank strings, and the default value of a
    /// non-nullable value type. On a nullable property only null means "not set", so an explicit
    /// <c>false</c> / <c>0</c> / zero-valued enum is a criterion.
    /// </summary>
    public static Expression<Func<TEntity, bool>> BuildFromDto<TEntity, TDto>(TDto dto, LogicalOperator logic = LogicalOperator.And)
        where TDto : class
    {
        return Build<TEntity>(BuildGroupFromDto(dto, logic));
    }

    /// <summary>
    /// Build a FilterGroup from a DTO instance decorated with [FilterProperty] attributes, skipping
    /// unset properties as <see cref="BuildFromDto{TEntity, TDto}"/> does.
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
            if (IsUnset(value, meta.PropertyInfo.PropertyType)) continue;

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

                result.Add(new FilterPropertyMetadata(prop, attr.EntityProperty, attr.Operator));
            }

            return result.ToArray();
        });
    }

    /// <summary>
    /// 属性没有给值：null、空白字符串、非可空值类型的默认值。可空类型只有 null 算没给值：
    /// 否则 <c>bool? IsEnabled = false</c> 这样的条件会被丢掉、查回全部行。
    /// </summary>
    private static bool IsUnset(object? value, Type propertyType)
    {
        if (value == null)
            return true;
        if (value is string str)
            return string.IsNullOrWhiteSpace(str);
        if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) != null)
            return false;

        return value.Equals(Activator.CreateInstance(propertyType));
    }

    private sealed record FilterPropertyMetadata(
        PropertyInfo PropertyInfo,
        string? EntityProperty,
        FilterOperator Operator);

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
            ? CombineBalanced(expressions, Expression.AndAlso)
            : CombineBalanced(expressions, Expression.OrElse);
    }

    /// <summary>
    /// 按原顺序两两合并成平衡二叉树：深度是 log2(n) 而不是 n。左深链让一组 n 条规则变成 n 层深的表达式，
    /// EF 的翻译与 SQL 生成按深度递归。AndAlso / OrElse 满足结合律，从左到右的短路顺序不变。
    /// </summary>
    private static Expression CombineBalanced(IReadOnlyList<Expression> expressions, Func<Expression, Expression, Expression> combine)
    {
        var level = expressions;
        while (level.Count > 1)
        {
            var next = new List<Expression>((level.Count + 1) / 2);
            for (var i = 0; i < level.Count; i += 2)
            {
                next.Add(i + 1 < level.Count ? combine(level[i], level[i + 1]) : level[i]);
            }

            level = next;
        }

        return level[0];
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
        // 处理 System.Text.Json 反序列化产生的 JsonElement 类型（JSON 的 null 在这里变成 null，随后同一道检查）
        if (value is JsonElement jsonElement)
        {
            value = ConvertJsonElement(jsonElement, targetType);
        }

        if (value == null)
        {
            // 对于值类型，null 无法赋值，抛异常（In 的数组元素同样适用：否则 null 会被数组静默写成 0）
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
            {
                throw new InvalidOperationException(
                    $"Cannot convert null to non-nullable type '{targetType.Name}'.");
            }
            return null;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // 日期一律按 UTC 解释，且在「类型已匹配」的捷径之前处理：调用方直接给的 DateTime 也可能是 Local / Unspecified
        if (underlyingType == typeof(DateTime))
        {
            return ToUtcDateTime(value);
        }

        if (underlyingType == typeof(DateTimeOffset))
        {
            return ToUtcDateTimeOffset(value);
        }

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

        // 特殊处理 TimeSpan
        if (underlyingType == typeof(TimeSpan) && value is string timeStr)
        {
            return TimeSpan.Parse(timeStr);
        }

        return Convert.ChangeType(value, underlyingType);
    }

    /// <summary>
    /// 日期过滤值按 UTC 解释：带偏移的字符串换算到 UTC，不带偏移的视为 UTC；
    /// 调用方给的 <c>Local</c> 换算到 UTC、<c>Unspecified</c> 标为 UTC。
    /// </summary>
    /// <remarks>
    /// 框架按 UTC 存时间。<c>DateTime.Parse</c> 不带样式时，<c>"...Z"</c> 得到换算成本机时区的 <c>Local</c>、
    /// 无偏移的得到 <c>Unspecified</c>：PostgreSQL 的 timestamptz 直接拒收这两种（查询执行期 500），
    /// 其它库在非 UTC 主机上则把条件整体平移一个时区偏移、安静地返回错的行。
    /// </remarks>
    private static DateTime ToUtcDateTime(object value) => value switch
    {
        DateTime dateTime => NormalizeToUtc(dateTime),
        DateTimeOffset offset => offset.UtcDateTime,
        string text => ParseUtcDateTime(text),
        _ => NormalizeToUtc((DateTime)Convert.ChangeType(value, typeof(DateTime), CultureInfo.InvariantCulture))
    };

    private static DateTimeOffset ToUtcDateTimeOffset(object value) => value switch
    {
        // Npgsql 写 timestamptz 只接受偏移为 0 的 DateTimeOffset；换成 UTC 不改变它表示的时刻
        DateTimeOffset offset => offset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(NormalizeToUtc(dateTime)),
        string text => ParseUtcDateTimeOffset(text),
        _ => ((DateTimeOffset)Convert.ChangeType(value, typeof(DateTimeOffset), CultureInfo.InvariantCulture)).ToUniversalTime()
    };

    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime ParseUtcDateTime(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static DateTimeOffset ParseUtcDateTimeOffset(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static Expression BuildArrayConstant(object? value, Type elementType)
    {
        var items = ReadCollectionValue(value);

        var arrayType = elementType.MakeArrayType();
        var array = Array.CreateInstance(elementType, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            array.SetValue(ConvertValue(items[i], elementType), i);
        }

        return Expression.Constant(array, arrayType);
    }

    /// <summary>
    /// 取出 In / NotIn 的候选值。
    /// </summary>
    /// <remarks>
    /// ★ 从 JSON 来的值（行级数据授权落库的规则、<c>[FromBody] PagedQuery</c>、<c>ApplyFilterFromJson</c>）是
    /// <see cref="JsonElement"/>：它是结构体、不实现 <see cref="System.Collections.IEnumerable"/>，
    /// 只按 <c>IEnumerable</c> 判断时这三条入口上的 In / NotIn 一律被判「不是集合」而 400。
    /// 字符串本身是 <c>IEnumerable&lt;char&gt;</c>，不排除的话 <c>In "abc"</c> 会按单个字符去匹配。
    /// </remarks>
    private static List<object?> ReadCollectionValue(object? value)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Array } jsonArray)
        {
            return jsonArray.EnumerateArray().Select(item => (object?)item).ToList();
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>().ToList();
        }

        throw new InvalidOperationException("Value for In/NotIn operator must be a collection.");
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
            // 数组只对 In / NotIn 有意义，它们在 ReadCollectionValue 里逐个元素转换，走不到这里
            JsonValueKind.Array => throw new InvalidOperationException("An array value is only valid for the In and NotIn operators."),
            _ => element.GetRawText()
        };
    }

    private static object ConvertJsonString(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(Guid)) return Guid.Parse(value);
        if (targetType == typeof(DateTime)) return ParseUtcDateTime(value);
        if (targetType == typeof(DateTimeOffset)) return ParseUtcDateTimeOffset(value);
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
}