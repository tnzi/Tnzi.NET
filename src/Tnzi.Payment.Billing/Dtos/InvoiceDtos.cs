namespace Tnzi.Payment.Billing.Dtos;

/// <summary>
/// 创建发票 DTO
/// </summary>
public class CreatePaymentInvoiceDto
{
    /// <summary>
    /// 发票类型
    /// </summary>
    public InvoiceType Type { get; set; } = InvoiceType.Standard;

    /// <summary>
    /// 账单归属用户ID（手工开票时指定，用户才能在"我的发票"里看到）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 客户名称。从支付创建发票时留空，由支付上的客户快照回填。
    /// </summary>
    [MaxLength(128)]
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
    [MaxLength(8)]
    public string? Currency { get; set; }

    /// <summary>
    /// 客户公司
    /// </summary>
    [MaxLength(256)]
    public string? CustomerCompany { get; set; }

    /// <summary>
    /// 客户税号
    /// </summary>
    [MaxLength(64)]
    public string? CustomerTaxId { get; set; }

    /// <summary>
    /// 客户地址
    /// </summary>
    [MaxLength(500)]
    public string? CustomerAddress { get; set; }

    /// <summary>
    /// 开票地址
    /// </summary>
    [MaxLength(500)]
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
    [MaxLength(64)]
    public string? TemplateName { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>
    /// 内部备注
    /// </summary>
    [MaxLength(1000)]
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
    [MaxLength(500)]
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
    /// 折扣金额（不得超过本行金额，见服务层校验）
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Discount amount cannot be negative.")]
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 税率（百分数，13 表示 13%）
    /// </summary>
    [Range(0, 100, ErrorMessage = "Tax rate must be a percentage between 0 and 100.")]
    public decimal TaxRate { get; set; }

    /// <summary>
    /// 税额
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// 产品代码
    /// </summary>
    [MaxLength(64)]
    public string? ProductCode { get; set; }

    /// <summary>服务期开始（含），订阅收款的明细行有。</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>服务期结束（不含）。</summary>
    public DateTime? PeriodEnd { get; set; }

    /// <summary>行的类别（例如超额存储行 <c>storage-overage</c>）；普通行为空。</summary>
    [MaxLength(PaymentLineDescription.KindMaxLength)]
    public string? Kind { get; set; }
}

/// <summary>
/// 发票 DTO
/// </summary>
public class PaymentInvoiceDto
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
    /// 账单归属用户ID（「我的发票」按它过滤）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 开出这张发票的支付ID（手工开票为 null）
    /// </summary>
    public Guid? PaymentId { get; set; }

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
    /// 客户税号
    /// </summary>
    public string? CustomerTaxId { get; set; }

    /// <summary>
    /// 客户地址（可多行）
    /// </summary>
    public string? CustomerAddress { get; set; }

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
    /// 已发出的次数（有上限，见 <c>PaymentConstants.MaxInvoiceSendCount</c>）
    /// </summary>
    public int SendCount { get; set; }

    /// <summary>
    /// 最后一次发出的时间（从未发出为 null）
    /// </summary>
    public DateTime? LastSentTime { get; set; }

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

    /// <summary>付款用的卡组织。</summary>
    public string? PaymentMethodBrand { get; set; }

    /// <summary>付款用的卡尾号。</summary>
    public string? PaymentMethodLast4 { get; set; }

    /// <summary>贷项通知单冲减的原发票。</summary>
    public Guid? OriginalInvoiceId { get; set; }

    /// <summary>开出这张贷项通知单的退款。</summary>
    public Guid? RefundId { get; set; }
}

/// <summary>
/// 发票查询 DTO
/// </summary>
public class PaymentInvoiceQueryDto : PagedQueryDto
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
    /// 备注（以 "Paid: …" 追加进发票的内部备注）
    /// </summary>
    [MaxLength(500, ErrorMessage = "Remark cannot exceed 500 characters.")]
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
    public PaymentInvoiceDto Invoice { get; set; } = new();
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyEmail { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;

    /// <summary>卖方资料：开票时的快照，引入快照之前开的票按当前配置。上面四个字段由它派生，留给既有模板。</summary>
    public InvoiceSellerDto Seller { get; set; } = new();

    /// <summary>
    /// 标题强调色，恒为合法的十六进制颜色：快照里的 <see cref="InvoiceSellerDto.AccentColor"/>，
    /// 没有或不合法时是出厂的 <see cref="InvoiceSellerOptions.DefaultAccentColor"/>。模板直接写进样式表。
    /// </summary>
    public string AccentColor { get; set; } = InvoiceSellerOptions.DefaultAccentColor;

    /// <summary>按税种拆开的税额（「HST 13%」带登记号）。没有拆分时为空，模板退回总税额一行。</summary>
    public List<TaxLineDto> TaxLines { get; set; } = new();

    /// <summary>贷项通知单冲减的原发票号。</summary>
    public string? OriginalInvoiceNo { get; set; }

    /// <summary>票据种类的标题（Invoice / Receipt / Credit Note）。</summary>
    public string DocumentTitle { get; set; } = "Invoice";
}

/// <summary>
/// 票据上的卖方资料（快照）。
/// </summary>
public class InvoiceSellerDto
{
    /// <summary>名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>地址，一行一项。</summary>
    public List<string> AddressLines { get; set; } = new();

    /// <summary>电话。</summary>
    public string? Phone { get; set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }

    /// <summary>网站。</summary>
    public string? Website { get; set; }

    /// <summary>标志（data URI，模板里直接放进 img 的 src）；没有标志时为空。</summary>
    public string? LogoDataUri { get; set; }

    /// <summary>税务登记号。</summary>
    public List<InvoiceTaxRegistrationDto> TaxRegistrations { get; set; } = new();

    /// <summary>标题强调色（<c>#RRGGBB</c> / <c>#RGB</c>）；没有配置时为空。</summary>
    public string? AccentColor { get; set; }
}

/// <summary>一个税务登记号。</summary>
public class InvoiceTaxRegistrationDto
{
    /// <summary>标签。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>号码。</summary>
    public string Number { get; set; } = string.Empty;
}

/// <summary>
/// 为一笔已成功的退款开贷项通知单（取自 <c>RefundProcessedEvent</c>）。
/// </summary>
public class CreateCreditNoteDto
{
    /// <summary>退款 Id（幂等键）。</summary>
    public Guid RefundId { get; set; }

    /// <summary>退款号。</summary>
    public string RefundNo { get; set; } = string.Empty;

    /// <summary>被退的支付。</summary>
    public Guid PaymentId { get; set; }

    /// <summary>退款金额（含税）。</summary>
    public decimal Amount { get; set; }

    /// <summary>币种。</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>退款落定时间。</summary>
    public DateTime RefundTime { get; set; }
}

/// <summary>
/// 发票产物的字节（PDF 或 HTML）。
/// </summary>
/// <remarks>
/// 存在的理由是「取地址」与「取内容」是两件事：只有 Storage 给得出 HTTP 可达的地址，
/// 而未加载 Storage 的宿主把产物写在本地磁盘上，任何地址都指不到它。
/// 下载端点因此直接吐字节，两种落地方式下都能用。
/// </remarks>
public class InvoiceDocumentDto
{
    /// <summary>文件内容</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>内容类型（<c>application/pdf</c> 或 <c>text/html</c>）</summary>
    public string ContentType { get; set; } = "application/pdf";

    /// <summary>建议的下载文件名</summary>
    public string FileName { get; set; } = string.Empty;
}
