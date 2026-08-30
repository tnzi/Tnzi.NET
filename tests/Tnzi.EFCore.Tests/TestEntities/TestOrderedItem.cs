namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 可排序测试实体：带一个分组键，用于验证重排的<b>范围谓词</b>确实生效
/// （同组内重排、其它组一动不动）。
/// </summary>
public class TestOrderedItem : EntityBase<Guid>, IHasOrder
{
    /// <summary>分组键（重排范围），null 表示未分组</summary>
    public Guid? GroupId { get; set; }

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public int SortOrder { get; set; }
}
