namespace Tnzi.Payment.Billing.Options;

/// <summary>
/// 发票配置选项
/// 配置路径：Payment:Invoice
/// </summary>
/// <remarks>
/// 拆分前这个类住在父模块的 <c>PaymentOptions.cs</c> 里，并且同时是 <c>PaymentOptions.Invoice</c>
/// 这个嵌套属性。搬来本程序集之后**配置面一个字都没变**：<see cref="ConfigSectionAttribute"/>
/// 写的是绝对节路径 <c>Payment:Invoice</c>，由本模块自己 <c>AddTnziOptions</c> 绑同一个节；
/// 运维手里的 appsettings.json 与配置中心里的键路径、分组键（<c>payment-invoice</c>）、
/// 模块名、图标、Order 全部照旧。
///
/// 少掉的只有嵌套属性那一条路径：父模块不再能写 <c>paymentOptions.Invoice.Enabled</c>
/// —— 它本来也没写过（发票服务与事件处理器一直是单独注入 <c>IOptions&lt;InvoiceOptions&gt;</c>）。
///
/// 顺带修好一处表里不一：配置中心的分组是从**已加载模块的程序集**扫出来的
/// （<c>AttributeSettingDefinitionProvider</c>），类留在父模块时，不加载本包的宿主
/// 仍会渲染出一个改了也不生效的 "Invoice" 分组。现在它随模块一起出现或消失。
/// </remarks>
[ConfigSection("Payment:Invoice")]
[RuntimeSettingGroup(Key = "payment-invoice", Module = "Payment", DisplayName = "Invoice",
    I18nKey = "admin.modules.system.settings.groups.paymentInvoice",
    Icon = "mdi:file-document-outline", Order = 520)]
public class InvoiceOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    [RuntimeSetting(Label = "Invoice Enabled", I18n = "admin.modules.system.settings.fields.paymentInvoiceEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Enable invoice generation")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认模板
    /// </summary>
    [RuntimeSetting(Label = "Default Invoice Template", I18n = "admin.modules.system.settings.fields.paymentInvoiceDefaultTemplate",
        Type = SettingFieldType.String,
        Description = "Template name used when none is specified")]
    public string DefaultTemplate { get; set; } = "InvoiceDefault";

    /// <summary>
    /// 支付成功后自动发送
    /// </summary>
    [RuntimeSetting(Label = "Auto-Send On Payment", I18n = "admin.modules.system.settings.fields.paymentInvoiceAutoSendOnPayment",
        Type = SettingFieldType.Boolean,
        Description = "Automatically generate and send an invoice when a payment succeeds")]
    public bool AutoSendOnPayment { get; set; } = true;

    /// <summary>
    /// 公司名称
    /// </summary>
    [RuntimeSetting(Label = "Company Name", I18n = "admin.modules.system.settings.fields.paymentInvoiceCompanyName",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? CompanyName { get; set; }

    /// <summary>
    /// 公司地址
    /// </summary>
    [RuntimeSetting(Label = "Company Address", I18n = "admin.modules.system.settings.fields.paymentInvoiceCompanyAddress",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? CompanyAddress { get; set; }

    /// <summary>
    /// 公司邮箱
    /// </summary>
    [RuntimeSetting(Label = "Company Email", I18n = "admin.modules.system.settings.fields.paymentInvoiceCompanyEmail",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? CompanyEmail { get; set; }

    /// <summary>
    /// 税号
    /// </summary>
    [RuntimeSetting(Label = "Tax ID", I18n = "admin.modules.system.settings.fields.paymentInvoiceTaxId",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? TaxId { get; set; }

    /// <summary>
    /// 订阅收款开收据而不是发票（<see cref="InvoiceType.Receipt"/> + <see cref="ReceiptTemplate"/>）：
    /// 已付的服务费给的是「收到了多少、哪张卡、哪一期」，不是一张待付的账单。默认 false，与此前相同。
    /// </summary>
    [RuntimeSetting(Label = "Receipts For Subscription Payments", I18n = "admin.modules.system.settings.fields.paymentInvoiceReceiptsForSubscriptions",
        Type = SettingFieldType.Boolean,
        Description = "Issue a receipt instead of an invoice for subscription payments")]
    public bool ReceiptsForSubscriptionPayments { get; set; }

    /// <summary>收据模板名（<c>Templates/Payment/Invoice/</c> 下，部署可按名覆盖）。</summary>
    [RuntimeSetting(Label = "Receipt Template", I18n = "admin.modules.system.settings.fields.paymentInvoiceReceiptTemplate",
        Type = SettingFieldType.String,
        Description = "Template used for receipts")]
    public string ReceiptTemplate { get; set; } = "ReceiptDefault";

    /// <summary>
    /// 退款成功后开一张贷项通知单冲减原发票（原发票不动，作废它就丢了「当初收过这笔钱」的凭据）。默认 false，与此前相同。
    /// </summary>
    [RuntimeSetting(Label = "Issue Credit Notes", I18n = "admin.modules.system.settings.fields.paymentInvoiceIssueCreditNotes",
        Type = SettingFieldType.Boolean,
        Description = "Issue a credit note against the original invoice when a refund succeeds")]
    public bool IssueCreditNotes { get; set; }

    /// <summary>贷项通知单模板名。</summary>
    [RuntimeSetting(Label = "Credit Note Template", I18n = "admin.modules.system.settings.fields.paymentInvoiceCreditNoteTemplate",
        Type = SettingFieldType.String,
        Description = "Template used for credit notes")]
    public string CreditNoteTemplate { get; set; } = "CreditNoteDefault";

    /// <summary>
    /// 卖方资料（标志、名称、多行地址、电话、邮箱、网站、税务登记号），开票那一刻快照到发票上：资料日后改了，
    /// 已开出的票据重新生成时印的仍是当时的卖方。没有配置 <see cref="InvoiceSellerOptions.Name"/> 时退回上面
    /// 四个公司字段（<see cref="CompanyName"/> 等），与此前相同。
    /// </summary>
    /// <remarks>刻意不进配置中心：多行地址、登记号列表与文件引用不是一个表单字段能表达的。</remarks>
    public InvoiceSellerOptions Seller { get; set; } = new();
}
