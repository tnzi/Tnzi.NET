namespace Tnzi.Data.Filtering;

/// <summary>
/// 筛选条件组 - 支持嵌套分组的过滤条件
/// 设计遵循组合模式，支持任意深度嵌套
/// </summary>
public class FilterGroup
{
    /// <summary>
    /// 组内条件间的逻辑运算符
    /// </summary>
    public LogicalOperator Logic { get; set; } = LogicalOperator.And;

    /// <summary>
    /// 过滤规则集合（叶子节点）
    /// </summary>
    public List<FilterRule> Rules { get; set; } = [];

    /// <summary>
    /// 子分组集合（支持嵌套）
    /// </summary>
    public List<FilterGroup> Groups { get; set; } = [];

    /// <summary>
    /// 是否有有效的过滤条件
    /// </summary>
    public bool HasFilters => Rules.Count > 0 || Groups.Exists(g => g.HasFilters);

    #region Fluent API

    /// <summary>
    /// 添加过滤规则
    /// </summary>
    public FilterGroup AddRule(string field, FilterOperator op, object? value)
    {
        Rules.Add(new FilterRule(field, op, value));
        return this;
    }

    /// <summary>
    /// 添加过滤规则
    /// </summary>
    public FilterGroup AddRule(FilterRule rule)
    {
        Rules.Add(rule);
        return this;
    }

    /// <summary>
    /// 添加相等条件的快捷方法
    /// </summary>
    public FilterGroup WhereEqual(string field, object? value)
        => AddRule(field, FilterOperator.Equal, value);

    /// <summary>
    /// 添加不等条件的快捷方法
    /// </summary>
    public FilterGroup WhereNotEqual(string field, object? value)
        => AddRule(field, FilterOperator.NotEqual, value);

    /// <summary>
    /// 添加包含条件的快捷方法
    /// </summary>
    public FilterGroup WhereContains(string field, string value)
        => AddRule(field, FilterOperator.Contains, value);

    /// <summary>
    /// 添加大于条件的快捷方法
    /// </summary>
    public FilterGroup WhereGreaterThan(string field, object value)
        => AddRule(field, FilterOperator.GreaterThan, value);

    /// <summary>
    /// 添加大于等于条件的快捷方法
    /// </summary>
    public FilterGroup WhereGreaterThanOrEqual(string field, object value)
        => AddRule(field, FilterOperator.GreaterThanOrEqual, value);

    /// <summary>
    /// 添加小于条件的快捷方法
    /// </summary>
    public FilterGroup WhereLessThan(string field, object value)
        => AddRule(field, FilterOperator.LessThan, value);

    /// <summary>
    /// 添加小于等于条件的快捷方法
    /// </summary>
    public FilterGroup WhereLessThanOrEqual(string field, object value)
        => AddRule(field, FilterOperator.LessThanOrEqual, value);

    /// <summary>
    /// 添加开始于条件的快捷方法
    /// </summary>
    public FilterGroup WhereStartsWith(string field, string value)
        => AddRule(field, FilterOperator.StartsWith, value);

    /// <summary>
    /// 添加结束于条件的快捷方法
    /// </summary>
    public FilterGroup WhereEndsWith(string field, string value)
        => AddRule(field, FilterOperator.EndsWith, value);

    /// <summary>
    /// 添加 In 条件的快捷方法
    /// </summary>
    public FilterGroup WhereIn<T>(string field, IEnumerable<T> values)
    {
        // 优化：如果已经是数组，直接使用；否则转换为数组
        // 这样可以避免不必要的数组复制
        object arrayValue = values is T[] array ? array : values.ToArray();
        return AddRule(field, FilterOperator.In, arrayValue);
    }

    /// <summary>
    /// 添加 IsNull 条件的快捷方法
    /// </summary>
    public FilterGroup WhereIsNull(string field)
        => AddRule(field, FilterOperator.IsNull, null);

    /// <summary>
    /// 添加 IsNotNull 条件的快捷方法
    /// </summary>
    public FilterGroup WhereIsNotNull(string field)
        => AddRule(field, FilterOperator.IsNotNull, null);

    /// <summary>
    /// 添加子分组
    /// </summary>
    public FilterGroup AddGroup(FilterGroup group)
    {
        Groups.Add(group);
        return this;
    }

    /// <summary>
    /// 添加子分组（使用 Action 配置）
    /// </summary>
    public FilterGroup AddGroup(LogicalOperator logic, Action<FilterGroup> configure)
    {
        var group = new FilterGroup { Logic = logic };
        configure(group);
        Groups.Add(group);
        return this;
    }

    #endregion

    #region Static Factory Methods

    /// <summary>
    /// 创建 AND 组
    /// </summary>
    public static FilterGroup And() => new() { Logic = LogicalOperator.And };

    /// <summary>
    /// 创建 OR 组
    /// </summary>
    public static FilterGroup Or() => new() { Logic = LogicalOperator.Or };

    /// <summary>
    /// 创建 AND 组（使用 Action 配置）
    /// </summary>
    public static FilterGroup And(Action<FilterGroup> configure)
    {
        var group = And();
        configure(group);
        return group;
    }

    /// <summary>
    /// 创建 OR 组（使用 Action 配置）
    /// </summary>
    public static FilterGroup Or(Action<FilterGroup> configure)
    {
        var group = Or();
        configure(group);
        return group;
    }

    #endregion
}
