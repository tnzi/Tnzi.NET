namespace Tnzi.Payment.Entities;

/// <summary>
/// 发票明细实体
/// </summary>
public class InvoiceLineItem : EntityBase<Guid>
{
    /// <summary>
    /// 关联发票ID
    /// </summary>
    public Guid InvoiceId { get; set; }

    /// <summary>
    /// 关联发票实体
    /// </summary>
    public virtual Invoice? Invoice { get; set; }

    /// <summary>
    /// 行号
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// 单价
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 税率
    /// </summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// 税额
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// 产品代码
    /// </summary>
    public string? ProductCode { get; set; }
}
