namespace Tnzi.Data;

/// <summary>
/// 排序条件
/// </summary>
public class SortCondition
{
    /// <summary>
    /// 初始化一个<see cref="SortCondition"/>类型的新实例
    /// </summary>
    public SortCondition()
    {
    }

    /// <summary>
    /// 初始化一个<see cref="SortCondition"/>类型的新实例
    /// </summary>
    /// <param name="sortField">排序字段名</param>
    /// <param name="listSortDirection">排序方向</param>
    public SortCondition(string sortField, ListSortDirection listSortDirection)
    {
        SortField = sortField;
        ListSortDirection = listSortDirection;
    }

    /// <summary>
    /// 获取或设置 排序字段名
    /// </summary>
    public string SortField { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 排序方向
    /// </summary>
    public ListSortDirection ListSortDirection { get; set; } = ListSortDirection.Ascending;
}
