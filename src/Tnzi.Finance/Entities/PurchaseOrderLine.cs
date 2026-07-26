namespace Tnzi.Finance.Entities;

/// <summary>
/// 采购订单行（草稿硬删重建，同账单行范式）
/// </summary>
public class PurchaseOrderLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属采购订单</summary>
    public Guid PurchaseOrderId { get; set; }

    /// <summary>行号（1 起）</summary>
    public int LineNumber { get; set; }

    /// <summary>目录项（可空 = 自由行）</summary>
    public Guid? ItemId { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>费用/资产科目覆盖（转换成账单时原样带过去）</summary>
    public Guid? AccountId { get; set; }

    /// <summary>数量</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>单价（交易币）</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>行金额（交易币 = 舍入(数量 × 单价)）</summary>
    public decimal Amount { get; set; }

    /// <summary>税码（null = 免税行）</summary>
    public Guid? TaxCodeId { get; set; }
}
