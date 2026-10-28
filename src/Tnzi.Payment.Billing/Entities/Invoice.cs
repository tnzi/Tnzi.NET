namespace Tnzi.Payment.Billing.Entities;

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
    /// 关联支付实体。
    /// </summary>
    /// <remarks>
    /// 方向是「本模块 → 父模块」：发票认识支付，支付对发票一无所知。反向导航属性已从
    /// <c>Payment</c> 上删除，一对一关系改由 <c>InvoiceConfiguration</c> 从依赖端声明 ——
    /// 少了这条边，父模块才可能在不加载本包时照常编译与运行。
    /// 类型写成别名是因为 <c>Payment</c> 这个标识符在本命名空间里先解析到 <c>Tnzi.Payment</c>
    /// 这个**命名空间**（查找从内层命名空间逐级向外，命名空间成员先于 using 命中）。
    /// </remarks>
    public virtual PaymentEntity? Payment { get; set; }

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
    /// 发票记在哪个计费账户上：种类（与开出它的那笔支付相同）。没有归属的发票为空。
    /// 「我的发票」与发票下载按账户认主：单客户部署里任何一位管理者都能看到整个机构的发票。
    /// </summary>
    public string? BillingAccountType { get; set; }

    /// <summary>
    /// 计费账户 Id（与 <see cref="BillingAccountType"/> 成对）。
    /// </summary>
    public string? BillingAccountId { get; set; }

    /// <summary>客户名称的列宽</summary>
    public const int CustomerNameMaxLength = 128;

    /// <summary>客户公司的列宽</summary>
    public const int CustomerCompanyMaxLength = 256;

    /// <summary>客户税号的列宽</summary>
    public const int CustomerTaxIdMaxLength = 64;

    /// <summary>客户地址的列宽</summary>
    public const int CustomerAddressMaxLength = 500;

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
    public string? PdfFilePath { get; set; }

    /// <summary>
    /// PDF文件URL
    /// </summary>
    public string? PdfFileUrl { get; set; }

    /// <summary>
    /// Storage 模块中的 PDF 文件记录ID（多实例/容器部署下持久化引用）。
    /// </summary>
    /// <remarks>
    /// ★ 刻意不标 <c>[FileField]</c>：三个产物列只经条件更新写入（不走变更跟踪，标了也追踪不到），
    /// 引用由发票服务显式维护：生成时 <c>SaveWithReferenceAsync</c> 建引用，替换或作废时解除旧引用。
    /// 再叠一层按字段追踪的引用会把同一个文件计两次。此前 <c>[FileField]</c> 标在路径与 URL 两个字符串上，
    /// 它们永远解析不出文件 Id，标了等于没标。
    /// </remarks>
    public Guid? PdfFileId { get; set; }

    /// <summary>
    /// 开票那一刻的卖方资料（<see cref="Dtos.InvoiceSellerDto"/> 的 JSON，标志已嵌成 data URI）。
    /// 为空的是引入快照之前开的票，渲染时按当前配置。
    /// </summary>
    public string? SellerSnapshotJson { get; set; }

    /// <summary>按税种拆开的税额（取自支付，<see cref="TaxLineDto"/> 列表的 JSON）。</summary>
    public string? TaxLinesJson { get; set; }

    /// <summary>付款用的卡组织（收据上印）。</summary>
    public string? PaymentMethodBrand { get; set; }

    /// <summary>付款用的卡尾号。</summary>
    public string? PaymentMethodLast4 { get; set; }

    /// <summary>贷项通知单冲减的原发票。</summary>
    public Guid? OriginalInvoiceId { get; set; }

    /// <summary>开出这张贷项通知单的退款（按它幂等：一笔退款至多一张）。</summary>
    public Guid? RefundId { get; set; }

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
