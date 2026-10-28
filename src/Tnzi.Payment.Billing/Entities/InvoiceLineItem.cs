namespace Tnzi.Payment.Billing.Entities;

/// <summary>
/// 发票明细实体
/// </summary>
public class InvoiceLineItem : EntityBase<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

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

    /// <summary>服务期开始（含），订阅收款的明细行有。</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>服务期结束（不含）。</summary>
    public DateTime? PeriodEnd { get; set; }

    /// <summary>行的类别（来自 <c>PaymentLineDescription.Kind</c>，例如超额存储行 <c>storage-overage</c>）；普通行为空。</summary>
    public string? Kind { get; set; }
}
