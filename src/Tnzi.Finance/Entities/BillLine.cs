namespace Tnzi.Finance.Entities;

/// <summary>
/// 采购账单行
/// </summary>
public class BillLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属账单</summary>
    public Guid BillId { get; set; }

    /// <summary>行号</summary>
    public int LineNumber { get; set; }

    /// <summary>目录项</summary>
    public Guid? ItemId { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>费用科目覆盖（null 回退 Item.ExpenseAccountId）</summary>
    public Guid? AccountId { get; set; }

    /// <summary>数量</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>单价</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>行金额</summary>
    public decimal Amount { get; set; }

    /// <summary>税码</summary>
    public Guid? TaxCodeId { get; set; }

    /// <summary>手动税额覆盖（null = 按税率计算；仅在行有税码时合法，>= 0）</summary>
    public decimal? TaxAmount { get; set; }
}
