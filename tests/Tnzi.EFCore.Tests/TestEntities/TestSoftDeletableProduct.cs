// -----------------------------------------------------------------------
// <copyright file="TestSoftDeletableProduct.cs" company="Tnzi">
//     Copyright (c) Tnzi. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------


namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 支持软删除的测试产品实体
/// </summary>
public class TestSoftDeletableProduct : EntityBase<Guid>, ISoftDelete
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

    /// <summary>
    /// 是否已删除
    /// </summary>
    public bool IsDeleted { get; set; }
}
