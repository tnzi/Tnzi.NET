namespace Tnzi.Data.Filtering;

/// <summary>
/// 筛选规则 - 单个过滤条件
/// </summary>
public class FilterRule
{
    /// <summary>
    /// 无参构造函数（用于 JSON 反序列化）
    /// </summary>
    public FilterRule() { }

    /// <summary>
    /// 创建过滤规则
    /// </summary>
    /// <param name="field">属性名称（支持嵌套属性，如 "Author.Name"）</param>
    /// <param name="op">操作符</param>
    /// <param name="value">值</param>
    public FilterRule(string field, FilterOperator op, object? value)
    {
        Field = field;
        Operator = op;
        Value = value;
    }

    /// <summary>
    /// 属性名称（支持嵌套属性，如 "Author.Name"）
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 操作符
    /// </summary>
    public FilterOperator Operator { get; set; } = FilterOperator.Equal;

    /// <summary>
    /// 值
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 创建相等条件
    /// </summary>
    public static FilterRule Equal(string field, object? value)
        => new(field, FilterOperator.Equal, value);

    /// <summary>
    /// 创建不等条件
    /// </summary>
    public static FilterRule NotEqual(string field, object? value)
        => new(field, FilterOperator.NotEqual, value);

    /// <summary>
    /// 创建包含条件（字符串）
    /// </summary>
    public static FilterRule Contains(string field, string value)
        => new(field, FilterOperator.Contains, value);

    /// <summary>
    /// 创建大于条件
    /// </summary>
    public static FilterRule GreaterThan(string field, object value)
        => new(field, FilterOperator.GreaterThan, value);

    /// <summary>
    /// 创建小于条件
    /// </summary>
    public static FilterRule LessThan(string field, object value)
        => new(field, FilterOperator.LessThan, value);

    public override bool Equals(object? obj)
    {
        if (obj is not FilterRule rule) return false;
        return rule.Field == Field && Equals(rule.Value, Value) && rule.Operator == Operator;
    }

    public override int GetHashCode() => HashCode.Combine(Field, Value, Operator);
}
