namespace Tnzi.Finance.Entities;

/// <summary>
/// 销售发票行（草稿硬删重建，同凭证行范式）
/// </summary>
public class InvoiceLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属发票</summary>
    public Guid InvoiceId { get; set; }

    /// <summary>行号（1 起）</summary>
    public int LineNumber { get; set; }

    /// <summary>目录项（可空 = 自由行）</summary>
    public Guid? ItemId { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>收入科目覆盖（null 回退 Item.IncomeAccountId）</summary>
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
