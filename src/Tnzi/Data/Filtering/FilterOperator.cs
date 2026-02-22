namespace Tnzi.Data;

/// <summary>
/// 过滤操作符
/// </summary>
public enum FilterOperator
{
    /// <summary>
    /// 等于
    /// </summary>
    Equal,

    /// <summary>
    /// 不等于
    /// </summary>
    NotEqual,

    /// <summary>
    /// 大于
    /// </summary>
    GreaterThan,

    /// <summary>
    /// 大于等于
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// 小于
    /// </summary>
    LessThan,

    /// <summary>
    /// 小于等于
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// 包含（字符串）
    /// </summary>
    Contains,

    /// <summary>
    /// 不包含（字符串）
    /// </summary>
    NotContains,

    /// <summary>
    /// 开始于（字符串）
    /// </summary>
    StartsWith,

    /// <summary>
    /// 结束于（字符串）
    /// </summary>
    EndsWith,

    /// <summary>
    /// 在范围内（集合）
    /// </summary>
    In,

    /// <summary>
    /// 不在范围内（集合）
    /// </summary>
    NotIn,

    /// <summary>
    /// 为空
    /// </summary>
    IsNull,

    /// <summary>
    /// 不为空
    /// </summary>
    IsNotNull
}

