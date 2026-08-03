namespace Tnzi.Payment.Entities;

/// <summary>
/// 发票实体
/// </summary>
public class Invoice : MultiTenantAuditedEntity<Guid>
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
    /// 账单归属用户ID。
    /// 与审计字段 CreatorId 分开：自动开票发生在回调/后台任务里，CreatorId 为空，
    /// 只靠它做归属判定会让用户在"我的发票"里看不到自己的发票。
    /// </summary>
    public Guid? UserId { get; set; }

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
    /// PDF文件路径（Storage 模块加载时为存储后端相对路径/URL；否则为本地回退路径）
    /// </summary>
    [FileField]
    public string? PdfFilePath { get; set; }

    /// <summary>
    /// PDF文件URL
    /// </summary>
    [FileField]
    public string? PdfFileUrl { get; set; }

    /// <summary>
    /// Storage 模块中的 PDF 文件记录ID（多实例/容器部署下持久化引用；引用追踪由 Storage 模块负责）
    /// </summary>
    public Guid? PdfFileId { get; set; }

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
        return $"INV{IdHelper.NextId()}";
    }
}
