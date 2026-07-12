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
