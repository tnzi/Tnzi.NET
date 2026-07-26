namespace Tnzi.Finance.Options;

/// <summary>
/// 财务模块配置
/// </summary>
/// <remarks>
/// BaseCurrency 是账本的本位币：所有总账行同时存储交易币金额与本位币金额，
/// 报表按本位币聚合。**账本产生分录后不得更改本位币**（历史分录不会重算），
/// 因此该项为纯启动配置，不提供运行时热设置。
///
/// 热配字段说明：FinanceOptions 是财务全部运行时配置的**唯一消费源**（服务经
/// <c>IOptionsSnapshot&lt;FinanceOptions&gt;</c> 按请求热读）。为了在配置中心把
/// 记账/GL 类阈值单独归到「Accounting」导航组，会计类热字段的**分组定义**由
/// <see cref="FinanceAccountingOptions"/> 承载（同 <c>Finance</c> section、同默认值，
/// 仅供 AttributeSettingDefinitionProvider 扫描出组元数据，不参与 DI 消费）。
/// </remarks>
[ConfigSection("Finance")]
[RuntimeSettingGroup(Key = "finance-general", Module = "Finance", DisplayName = "General",
    I18nKey = "admin.modules.system.settings.groups.financeGeneral",
    Icon = "mdi:receipt-text-outline", Order = 550)]
public class FinanceOptions
{
    /// <summary>
    /// 本位币（ISO 4217 货币代码，默认 USD）
    /// </summary>
    /// <remarks>账本产生分录后不可变（历史分录不重算），故不作为热设置暴露。</remarks>
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>
    /// 本位币金额舍入小数位（多币种换算后按此位数舍入，默认 2）
    /// </summary>
    /// <remarks>热设置定义见 <see cref="FinanceAccountingOptions"/>（Accounting 组）。</remarks>
    public int BaseCurrencyDecimals { get; set; } = 2;

    /// <summary>
    /// 过账配平容差（本位币金额绝对值）。
    /// 多币种换算后借贷尾差在容差内时，自动生成舍入差额行
    /// （记入 <see cref="Metadata.AccountSystemRole.RoundingDifference"/> 角色科目）；
    /// 超出容差则拒绝过账
    /// </summary>
    /// <remarks>热设置定义见 <see cref="FinanceAccountingOptions"/>（Accounting 组）。</remarks>
    public decimal RoundingTolerance { get; set; } = 0.05m;

    /// <summary>
    /// 凭证编号前缀
    /// </summary>
    [RuntimeSetting(Label = "Journal Number Prefix", I18n = "admin.modules.system.settings.fields.journalNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for journal entry numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string JournalNumberPrefix { get; set; } = "JE-";

    /// <summary>
    /// 凭证编号数字部分补零位数（0 表示不补零）
    /// </summary>
    /// <remarks>
    /// GL 运行余额与报表按凭证号**字符串排序**依赖补零（无补零时 "JE-10" 会排在 "JE-9" 之前），
    /// 属排序不变量，不作为热设置暴露。
    /// </remarks>
    public int JournalNumberPadding { get; set; } = 6;

    /// <summary>
    /// 过账是否要求过账日期落在已定义且未关闭的会计年度内。
    /// false（默认）：未定义会计年度时允许任意日期过账（零配置可用），
    /// 已定义时仅校验"不得落入已关闭年度"；
    /// true：过账日期必须落在某个未关闭的会计年度内
    /// </summary>
    /// <remarks>热设置定义见 <see cref="FinanceAccountingOptions"/>（Accounting 组）。</remarks>
    public bool RequireFiscalYearForPosting { get; set; }

    /// <summary>
    /// 单张凭证最大分录行数（防御性上限）
    /// </summary>
    /// <remarks>热设置定义见 <see cref="FinanceAccountingOptions"/>（Accounting 组）。</remarks>
    public int MaxLinesPerEntry { get; set; } = 500;

    /// <summary>
    /// 默认付款账期天数（客户/供应商未单独配置时使用，P2 单据据此推 DueDate）
    /// </summary>
    [RuntimeSetting(Label = "Default Payment Terms (days)", I18n = "admin.modules.system.settings.fields.defaultPaymentTermsDays",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Documents",
        Description = "Default number of days used to derive a document due date when the party has no term configured.")]
    public int DefaultPaymentTermsDays { get; set; } = 30;

    /// <summary>发票编号前缀（数字部分补零位数沿用 <see cref="JournalNumberPadding"/>）</summary>
    [RuntimeSetting(Label = "Invoice Number Prefix", I18n = "admin.modules.system.settings.fields.invoiceNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for invoice numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string InvoiceNumberPrefix { get; set; } = "INV-";

    /// <summary>账单编号前缀</summary>
    [RuntimeSetting(Label = "Bill Number Prefix", I18n = "admin.modules.system.settings.fields.billNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for bill numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string BillNumberPrefix { get; set; } = "BILL-";

    /// <summary>费用支出编号前缀</summary>
    [RuntimeSetting(Label = "Expense Number Prefix", I18n = "admin.modules.system.settings.fields.expenseNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for expense numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string ExpenseNumberPrefix { get; set; } = "EXP-";

    /// <summary>贷项单编号前缀</summary>
    [RuntimeSetting(Label = "Credit Memo Number Prefix", I18n = "admin.modules.system.settings.fields.creditMemoNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for credit memo numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string CreditMemoNumberPrefix { get; set; } = "CM-";

    /// <summary>
    /// 账龄分桶的默认切分点（天）。
    /// </summary>
    /// <remarks>
    /// 不作为 <see cref="AgingBucketDays"/> 的属性初始值，理由见该属性。
    /// </remarks>
    public static readonly int[] DefaultAgingBucketDays = [30, 60, 90];

    /// <summary>
    /// 账龄分桶的三个切分点（天），留空表示用 <see cref="DefaultAgingBucketDays"/>（30 / 60 / 90）。
    /// </summary>
    /// <remarks>
    /// 30/60/90 是北美惯例但不是法律：按周结算的行业常用 7/14/21，工程行业按
    /// 合同里程碑分。桶的**数量固定为五档**（Current + 三段 + 超期），只有切分点
    /// 可配——桶数可变意味着 DTO 形状可变，那会把每个消费端的报表列都变成动态的。
    ///
    /// ★**默认值必须是空数组**：.NET 的配置绑定对数组是**追加**语义（先复制现有元素、
    /// 再把绑定到的元素接在后面）。若这里预置 [30,60,90]，任何配了
    /// <c>Finance:AgingBucketDays: [7,14,21]</c> 的部署都会绑成六个元素、随即撞上
    /// 下面那条"必须恰好三个"的校验而**启动失败**，且错误信息指向操作员那份（正确的）
    /// 配置。缺省值改在读取处补（见 <see cref="ResolveAgingBucketDays"/>）。
    ///
    /// 非法配置（非升序、非正数、数量既不是 0 也不是 3）在启动校验时拒绝，不静默回退：
    /// 悄悄用回默认值，会让人以为自己配的口径生效了。
    /// </remarks>
    public int[] AgingBucketDays { get; set; } = [];

    /// <summary>
    /// 取生效的账龄切分点：配了就用配的，没配用 <see cref="DefaultAgingBucketDays"/>。
    /// </summary>
    public int[] ResolveAgingBucketDays()
        => AgingBucketDays is { Length: 3 } cuts ? cuts : DefaultAgingBucketDays;

    /// <summary>逾期多少天开始算"已逾期"（低于此值只作友好提醒）</summary>
    [RuntimeSetting(Label = "Dunning: Overdue After (days)", I18n = "admin.modules.system.settings.fields.dunningOverdueDays",
        Type = SettingFieldType.Int, Subsection = "Dunning",
        Description = "Days past due before a receivable is escalated from a reminder to overdue.")]
    public int DunningOverdueDays { get; set; } = 30;

    /// <summary>逾期多少天发最后通知</summary>
    [RuntimeSetting(Label = "Dunning: Final Notice After (days)", I18n = "admin.modules.system.settings.fields.dunningFinalNoticeDays",
        Type = SettingFieldType.Int, Subsection = "Dunning",
        Description = "Days past due before a receivable is escalated to a final notice.")]
    public int DunningFinalNoticeDays { get; set; } = 60;

    /// <summary>
    /// 低于此金额不催。
    /// </summary>
    /// <remarks>
    /// 为了三块钱发最后通知，只会让对方不再认真看这类邮件。
    /// </remarks>
    [RuntimeSetting(Label = "Dunning: Minimum Amount", I18n = "admin.modules.system.settings.fields.dunningMinimumAmount",
        Type = SettingFieldType.Decimal, Subsection = "Dunning",
        Description = "Overdue amounts below this are not worth chasing and report no dunning level.")]
    public decimal DunningMinimumAmount { get; set; } = 1m;

    /// <summary>单张单据最多挂几个附件</summary>
    [RuntimeSetting(Label = "Max Attachments Per Document", I18n = "admin.modules.system.settings.fields.maxAttachmentsPerDocument",
        Type = SettingFieldType.Int, Subsection = "Attachments",
        Description = "Upper bound on files attached to a single finance document.")]
    public int MaxAttachmentsPerDocument { get; set; } = 20;

    /// <summary>
    /// 允许挂的内容类型白名单；**空 = 不限**。
    /// </summary>
    /// <remarks>
    /// 刻意不设默认白名单：多数部署并不想管这件事，给一份"合理默认"只会让第一个
    /// 挂 .heic 收据的人撞墙。需要收紧的部署自己列。
    /// 数组型配置不进设置中心（那里没有数组控件），只从 appsettings 绑定。
    /// </remarks>
    public string[] AllowedAttachmentContentTypes { get; set; } = [];

    /// <summary>
    /// 精确参考号匹配的置信度（默认 1.0）。
    /// </summary>
    /// <remarks>
    /// 置信度是给**人**看的判断依据（界面显示成"精确"/"可能"），不是阈值——引擎
    /// 只在候选唯一时才建议，所以数值本身不改变匹配结果。放出来是为了让接了自己
    /// 匹配逻辑的部署能与内置规则用同一把尺子。
    /// </remarks>
    public decimal ExactMatchConfidence { get; set; } = 1.0m;

    /// <summary>金额+日期窗口匹配的置信度（默认 0.8）</summary>
    public decimal AmountDateMatchConfidence { get; set; } = 0.8m;

    /// <summary>报价单编号前缀</summary>
    [RuntimeSetting(Label = "Estimate Number Prefix", I18n = "admin.modules.system.settings.fields.estimateNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for estimate (quote) numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string EstimateNumberPrefix { get; set; } = "EST-";

    /// <summary>采购订单编号前缀</summary>
    [RuntimeSetting(Label = "Purchase Order Number Prefix", I18n = "admin.modules.system.settings.fields.purchaseOrderNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for purchase order numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string PurchaseOrderNumberPrefix { get; set; } = "PO-";

    /// <summary>收付款单编号前缀</summary>
    [RuntimeSetting(Label = "Payment Number Prefix", I18n = "admin.modules.system.settings.fields.paymentNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for payment entry numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string PaymentNumberPrefix { get; set; } = "PMT-";

    /// <summary>
    /// 收款（Inbound）未指定存入科目时是否过账到待存款项（UndepositedFunds 角色科目）。
    /// false（默认）：必须显式指定存入科目
    /// </summary>
    [RuntimeSetting(Label = "Post to Undeposited Funds", I18n = "admin.modules.system.settings.fields.postToUndepositedFunds",
        Type = SettingFieldType.Boolean, Subsection = "Documents",
        Description = "When an inbound payment has no deposit account, post it to the UndepositedFunds role account instead of rejecting it.")]
    public bool PostToUndepositedFunds { get; set; }

    /// <summary>
    /// 报表 CSV 导出最大行数（总账明细全量导出超限时拒绝并提示缩小期间，
    /// 不做静默截断——截断的运行余额会误导对账）
    /// </summary>
    [RuntimeSetting(Label = "Report Export Max Rows", I18n = "admin.modules.system.settings.fields.reportExportMaxRows",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Documents",
        Description = "Maximum rows allowed in a CSV export; a larger range is rejected (never silently truncated) to avoid misleading running balances.")]
    public int ReportExportMaxRows { get; set; } = 50000;

    /// <summary>资金划转单编号前缀</summary>
    [RuntimeSetting(Label = "Transfer Number Prefix", I18n = "admin.modules.system.settings.fields.transferNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for fund transfer numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string TransferNumberPrefix { get; set; } = "TRF-";

    /// <summary>EFT 批次编号前缀（P3 EFT 输出）</summary>
    [RuntimeSetting(Label = "EFT Batch Number Prefix", I18n = "admin.modules.system.settings.fields.eftNumberPrefix",
        Type = SettingFieldType.String, Subsection = "Numbering",
        Description = "Prefix for EFT batch numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string EftNumberPrefix { get; set; } = "EFT-";

    /// <summary>
    /// 银行流水匹配的日期窗口（天）。规则 2（金额相等 + 候选唯一）在此窗口内匹配。
    /// </summary>
    [RuntimeSetting(Label = "Bank Match Date Window (days)", I18n = "admin.modules.system.settings.fields.bankMatchDateWindowDays",
        Type = SettingFieldType.Int, Min = 0, Max = 90, Subsection = "Banking",
        Description = "Day window used when matching an imported bank transaction to a ledger line by amount. 0 requires the same day.")]
    public int BankMatchDateWindowDays { get; set; } = 7;

    /// <summary>
    /// 是否在存在 Draft 对账时对精确匹配（规则 1）自动确认生成勾选行。
    /// </summary>
    [RuntimeSetting(Label = "Auto-confirm Exact Bank Matches", I18n = "admin.modules.system.settings.fields.bankFeedAutoConfirmExactMatches",
        Type = SettingFieldType.Boolean, Subsection = "Banking",
        Description = "When enabled, an exact reference match auto-generates the reconciliation line if a draft reconciliation exists.")]
    public bool BankFeedAutoConfirmExactMatches { get; set; }

    /// <summary>
    /// 单次银行流水导入的最大行数（防御性上限，超限整批拒绝）。
    /// </summary>
    [RuntimeSetting(Label = "Bank Import Max Rows", I18n = "admin.modules.system.settings.fields.bankImportMaxRows",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Banking",
        Description = "Maximum number of transactions accepted in a single statement import; a larger file is rejected.")]
    public int BankImportMaxRows { get; set; } = 10000;

    /// <summary>
    /// E-13B MICR 字体文件路径（仅白纸全打印支票需要；预印票纸不打 MICR 行时留空）。
    /// </summary>
    [RuntimeSetting(Label = "Check MICR Font Path", I18n = "admin.modules.system.settings.fields.checkMicrFontPath",
        Type = SettingFieldType.String, Required = false, Subsection = "Checks",
        Description = "Filesystem path to an E-13B MICR TrueType font, required only when printing checks on blank stock.")]
    public string? CheckMicrFontPath { get; set; }

    /// <summary>
    /// 支票抬头的出票公司名（留空 = 取 System General 的 <c>System:CompanyName</c>）。
    /// </summary>
    [RuntimeSetting(Label = "Check Issuer Name", I18n = "admin.modules.system.settings.fields.checkIssuerName",
        Type = SettingFieldType.String, Required = false, Subsection = "Checks",
        Description = "Company name printed on the check letterhead. Leave empty to use the system-wide company name.")]
    public string? CheckIssuerName { get; set; }

    /// <summary>
    /// 支票抬头的出票公司地址（每行一条，留空 = 取 System General 的 <c>System:Address</c>）。
    /// </summary>
    [RuntimeSetting(Label = "Check Issuer Address", I18n = "admin.modules.system.settings.fields.checkIssuerAddress",
        Type = SettingFieldType.Text, Required = false, Subsection = "Checks",
        Description = "Address printed on the check letterhead, one line per row. Leave empty to use the system-wide address.")]
    public string? CheckIssuerAddress { get; set; }

    /// <summary>
    /// 签名图片地址（URL 或 data URI）。留空则只打签名线，由人工手签。
    /// </summary>
    [RuntimeSetting(Label = "Check Signature Image", I18n = "admin.modules.system.settings.fields.checkSignatureImageUrl",
        Type = SettingFieldType.String, Required = false, Subsection = "Checks",
        Description = "URL or data URI of the authorised-signature image. Leave empty to print an empty signature line for a wet signature.")]
    public string? CheckSignatureImageUrl { get; set; }

    /// <summary>签名人姓名（印在签名线下方）。</summary>
    [RuntimeSetting(Label = "Check Signature Name", I18n = "admin.modules.system.settings.fields.checkSignatureName",
        Type = SettingFieldType.String, Required = false, Subsection = "Checks",
        Description = "Name printed under the signature line.")]
    public string? CheckSignatureName { get; set; }

    /// <summary>签名人职务（印在签名人姓名下方）。</summary>
    [RuntimeSetting(Label = "Check Signature Title", I18n = "admin.modules.system.settings.fields.checkSignatureTitle",
        Type = SettingFieldType.String, Required = false, Subsection = "Checks",
        Description = "Job title printed under the signature name.")]
    public string? CheckSignatureTitle { get; set; }

    /// <summary>
    /// 报表是否从 AccountPeriodBalance 月粒度汇总桶读取聚合（默认 false）。
    /// 汇总维护无条件启用（每凭证过账/冲销同事务累加）；此开关仅门控读路径。
    /// </summary>
    /// <remarks>
    /// **存量账本启用前 MUST 先 POST admin/finance/balance-summary/rebuild** 重建历史桶，
    /// 否则报表会漏读汇总覆盖的历史月（新部署从空账本开始，开箱即真）。切换后经 verify 端点核实一致。
    /// </remarks>
    [RuntimeSetting(Label = "Use Balance Summary", I18n = "admin.modules.system.settings.fields.useBalanceSummary",
        Type = SettingFieldType.Boolean, Subsection = "Reports",
        Description = "Serve report aggregates from the account period-balance summary. On an existing ledger, rebuild the summary first before enabling.")]
    public bool UseBalanceSummary { get; set; }
}

/// <summary>
/// 财务「会计」设置组的**配置中心定义镜像**（非运行时消费类型）。
/// </summary>
/// <remarks>
/// 框架的 <see cref="RuntimeSettingGroupAttribute"/> 是类级特性（一个 Options 类 → 一个组），
/// 而记账/GL 类阈值与其余财务设置同处 <see cref="FinanceOptions"/>（且被过账引擎/校验器作为一个
/// 内聚单元消费）。为把这些字段单独归入配置中心的「Accounting」导航组，此类以相同的
/// <c>Finance</c> section + 相同默认值镜像这几个会计字段，供 AttributeSettingDefinitionProvider
/// 扫描出 <c>finance-accounting</c> 组元数据。
///
/// 铁律：<see cref="FinanceOptions"/> 是**唯一**运行时消费源；本类**不注入任何服务、不改运行时行为**，
/// 仅贡献分组定义。属性默认值必须与 <see cref="FinanceOptions"/> 对应字段保持一致（配置中心「恢复默认」据此显示）。
/// 保存时字段级 Min/Max 校验（与运行时 FinanceOptionsValidator 的数值下限等价）足以防止非法值经 reload
/// 触发绑定异常，故本镜像无需单独注册验证器。
/// </remarks>
[ConfigSection("Finance")]
[RuntimeSettingGroup(Key = "finance-accounting", Module = "Finance", DisplayName = "Accounting",
    I18nKey = "admin.modules.system.settings.groups.financeAccounting",
    Icon = "mdi:calculator-variant-outline", Order = 560)]
public class FinanceAccountingOptions
{
    /// <summary>本位币金额舍入小数位（镜像 <see cref="FinanceOptions.BaseCurrencyDecimals"/>）</summary>
    [RuntimeSetting(Label = "Base Currency Decimals", I18n = "admin.modules.system.settings.fields.baseCurrencyDecimals",
        Type = SettingFieldType.Int, Min = 0, Max = 4,
        Description = "Rounding decimals for base-currency amounts. Only affects newly posted entries; change with care.")]
    public int BaseCurrencyDecimals { get; set; } = 2;

    /// <summary>过账配平容差（镜像 <see cref="FinanceOptions.RoundingTolerance"/>）</summary>
    [RuntimeSetting(Label = "Rounding Tolerance", I18n = "admin.modules.system.settings.fields.roundingTolerance",
        Type = SettingFieldType.Decimal, Min = 0,
        Description = "Base-currency imbalance tolerance absorbed by a rounding-difference line. Only affects newly posted entries; change with care.")]
    public decimal RoundingTolerance { get; set; } = 0.05m;

    /// <summary>过账是否要求命中未关闭会计年度（镜像 <see cref="FinanceOptions.RequireFiscalYearForPosting"/>）</summary>
    [RuntimeSetting(Label = "Require Fiscal Year for Posting", I18n = "admin.modules.system.settings.fields.requireFiscalYearForPosting",
        Type = SettingFieldType.Boolean,
        Description = "When enabled, a posting date must fall inside a defined, open fiscal year. Only affects newly posted entries.")]
    public bool RequireFiscalYearForPosting { get; set; }

    /// <summary>单张凭证最大分录行数（镜像 <see cref="FinanceOptions.MaxLinesPerEntry"/>）</summary>
    [RuntimeSetting(Label = "Max Lines Per Entry", I18n = "admin.modules.system.settings.fields.maxLinesPerEntry",
        Type = SettingFieldType.Int, Min = 2,
        Description = "Defensive upper bound on journal lines per entry. Only affects newly posted entries.")]
    public int MaxLinesPerEntry { get; set; } = 500;
}
