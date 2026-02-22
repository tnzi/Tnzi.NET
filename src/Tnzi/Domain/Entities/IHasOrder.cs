namespace Tnzi.Domain.Entities;

/// <summary>
/// 可排序接口
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IHasOrder
{
    /// <summary>
    /// 排序序号
    /// </summary>
    int SortOrder { get; set; }
}
