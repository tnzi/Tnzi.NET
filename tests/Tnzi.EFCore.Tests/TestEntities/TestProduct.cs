
namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 测试产品实体 (简单实体,无审计和多租户)
/// </summary>
public class TestProduct : EntityBase<Guid>
{
    /// <summary>
    /// 产品名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 库存
    /// </summary>
    public int Stock { get; set; }
}