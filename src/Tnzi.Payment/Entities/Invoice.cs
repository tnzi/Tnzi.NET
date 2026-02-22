namespace Tnzi.Payment.Entities;

/// <summary>
/// 发票实体
/// </summary>
public class Invoice : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 发票号码
    /// </summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// 关联支付实体
    /// </summary>
    public virtual Payment? Payment { get; set; }

    /// <summary>
    /// 发票类型
    /// </summary>
    public InvoiceType Type { get; set; }

    /// <summary>
    /// 发票状态
    /// </summary>
    public InvoiceStatus Status { get; set; }

    /// <summary>
    /// 发票金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 税额
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 应付金额
    /// </summary>
    public decimal DueAmount { get; set; }

    /// <summary>
    /// 已付金额
    /// </summary>
    public decimal PaidAmount { get; set; }

    /// <summary>
    /// 客户名称
    /// </summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// 客户邮箱
    /// </summary>
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>
    /// 客户公司
    /// </summary>
    public string? CustomerCompany { get; set; }

    /// <summary>
    /// 客户税号
    /// </summary>
    public string? CustomerTaxId { get; set; }

    /// <summary>
    /// 客户地址
    /// </summary>
    public string? CustomerAddress { get; set; }

    /// <summary>
    /// 开票地址
    /// </summary>
    public string? BillingAddress { get; set; }

    /// <summary>
    /// 发票日期
    /// </summary>
    public DateTime InvoiceDate { get; set; }

    /// <summary>
    /// 到期日期
    /// </summary>
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// 支付日期
    /// </summary>
    public DateTime? PaidDate { get; set; }

    /// <summary>
    /// 模板名称
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>
    /// PDF文件路径
    /// </summary>
    public string? PdfFilePath { get; set; }

    /// <summary>
    /// PDF文件URL
    /// </summary>
    public string? PdfFileUrl { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// 内部备注
    /// </summary>
    public string? InternalNotes { get; set; }

    /// <summary>
    /// 发送次数
    /// </summary>
    public int SendCount { get; set; }

    /// <summary>
    /// 最后发送时间
    /// </summary>
    public DateTime? LastSentTime { get; set; }

    /// <summary>
    /// 税务信息（JSON格式）
    /// </summary>
    public string? TaxInfo { get; set; }

    /// <summary>
    /// 发票明细集合
    /// </summary>
    public virtual ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();

    /// <summary>
    /// 生成发票号码
    /// </summary>
    public static string GenerateInvoiceNo()
    {
        return $"INV{DateTime.UtcNow:yyyyMMdd}{Random.Shared.Next(10000, 99999)}";
    }
}
