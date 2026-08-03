namespace Tnzi.Payment.Dtos;

/// <summary>
/// 创建发票 DTO
/// </summary>
public class CreateInvoiceDto
{
    /// <summary>
    /// 发票类型
    /// </summary>
    public InvoiceType Type { get; set; } = InvoiceType.Standard;

    /// <summary>
    /// 关联支付ID（可选）
    /// </summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// 账单归属用户ID（手工开票时指定，用户才能在"我的发票"里看到）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 客户名称。从支付创建发票时留空，由支付上的客户快照回填。
    /// </summary>
    [MaxLength(256)]
    public string? CustomerName { get; set; }

    /// <summary>
    /// 客户邮箱。从支付创建发票时留空，由支付上的客户快照回填。
    /// </summary>
    [EmailAddress(ErrorMessage = "Customer email is not a valid email address.")]
    [MaxLength(256)]
    public string? CustomerEmail { get; set; }

    /// <summary>
    /// 币种；手工开票时必填，从支付创建时取支付币种
    /// </summary>
    public string? Currency { get; set; }

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
    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 到期日期
    /// </summary>
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// 模板名称
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// 内部备注
    /// </summary>
    public string? InternalNotes { get; set; }

    /// <summary>
    /// 发票明细。手工开票时必填；从支付创建时可留空，按支付金额生成单行。
    /// </summary>
    public List<InvoiceLineItemDto>? LineItems { get; set; }
}

/// <summary>
/// 发票明细 DTO
/// </summary>
public class InvoiceLineItemDto
{
    /// <summary>
    /// 描述
    /// </summary>
    [Required]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than 0.")]
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// 单价
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Unit price cannot be negative.")]
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

/// <summary>
/// 发票 DTO
/// </summary>
public class InvoiceDto
{
    /// <summary>
    /// 发票ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 发票号码
    /// </summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// 发票类型
    /// </summary>
    public InvoiceType Type { get; set; }

    /// <summary>
    /// 发票状态
    /// </summary>
    public InvoiceStatus Status { get; set; }

    /// <summary>
    /// 金额
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
    /// PDF文件URL
    /// </summary>
    public string? PdfFileUrl { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// 发票明细
    /// </summary>
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 发票查询 DTO
/// </summary>
public class InvoiceQueryDto : PagedQueryDto
{
    /// <summary>
    /// 发票号码
    /// </summary>
    public string? InvoiceNo { get; set; }

    /// <summary>
    /// 发票类型
    /// </summary>
    public InvoiceType? Type { get; set; }

    /// <summary>
    /// 发票状态
    /// </summary>
    public InvoiceStatus? Status { get; set; }

    /// <summary>
    /// 客户邮箱
    /// </summary>
    public string? CustomerEmail { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 标记发票已支付 DTO
/// </summary>
public class MarkInvoicePaidDto
{
    /// <summary>
    /// 支付金额
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0.")]
    public decimal PaidAmount { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 发送发票 DTO
/// </summary>
public class SendInvoiceDto
{
    /// <summary>
    /// 收件人邮箱
    /// </summary>
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    public string? RecipientEmail { get; set; }
}

/// <summary>
/// 取消发票 DTO
/// </summary>
public class CancelInvoiceDto
{
    /// <summary>
    /// 取消原因
    /// </summary>
    [MaxLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; set; }
}

/// <summary>
/// 发票 PDF DTO
/// </summary>
public class InvoicePdfDto
{
    public InvoiceDto Invoice { get; set; } = new();
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyEmail { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
}

/// <summary>
/// 发送邮件 DTO
/// </summary>
public class SendEmailDto
{
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
}
