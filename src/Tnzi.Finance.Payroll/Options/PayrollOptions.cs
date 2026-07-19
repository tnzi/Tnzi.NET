namespace Tnzi.Finance.Payroll.Options;

/// <summary>
/// 薪酬子模块配置
/// </summary>
/// <remarks>
/// 消费方一律经 <c>IOptionsSnapshot&lt;PayrollOptions&gt;</c> 按请求热读。
/// <c>MaxEmployeesPerRun</c> / <c>YtdBasis</c> / <c>ExternalAutoPost</c> 由 P4c 的
/// pay run 服务消费；在 P4c 接线宿主之前本模块不会被任何应用加载，配置中心不会提前暴露它们。
/// 编号数字部分补零位数沿用 <see cref="Tnzi.Finance.Options.FinanceOptions.JournalNumberPadding"/>。
/// </remarks>
[ConfigSection("Finance:Payroll")]
[RuntimeSettingGroup(Key = "finance-payroll", Module = "Finance", DisplayName = "Payroll",
    I18nKey = "admin.modules.system.settings.groups.financePayroll",
    Icon = "mdi:account-cash-outline", Order = 570)]
public class PayrollOptions
{
    /// <summary>发薪批次编号前缀（过账时分配，scope "PayRun"）</summary>
    [RuntimeSetting(Label = "Pay Run Number Prefix", I18n = "admin.modules.system.settings.fields.payRunNumberPrefix",
        Type = SettingFieldType.String,
        Description = "Prefix for pay run numbers. Change at period boundaries to avoid numbering continuity gaps.")]
    public string PayRunNumberPrefix { get; set; } = "PR-";

    /// <summary>单个发薪批次最大员工数（防御性上限）</summary>
    [RuntimeSetting(Label = "Max Employees Per Run", I18n = "admin.modules.system.settings.fields.maxEmployeesPerRun",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Defensive upper bound on the number of employees included in a single pay run.")]
    public int MaxEmployeesPerRun { get; set; } = 1000;

    /// <summary>薪资公式最大长度（字符数；组件/结构行公式与条件表达式共用）</summary>
    [RuntimeSetting(Label = "Formula Max Length", I18n = "admin.modules.system.settings.fields.formulaMaxLength",
        Type = SettingFieldType.Int, Min = 1, Max = 4000,
        Description = "Maximum length (in characters) accepted for salary formulas and condition expressions.")]
    public int FormulaMaxLength { get; set; } = 2000;

    /// <summary>Ytd() 年度累计口径（法定上限类公式依赖）</summary>
    [RuntimeSetting(Label = "YTD Basis", I18n = "admin.modules.system.settings.fields.ytdBasis",
        Description = "Year-to-date aggregation basis used by the Ytd() formula function.")]
    public YtdBasis YtdBasis { get; set; } = YtdBasis.CalendarYear;

    /// <summary>外部（embedded provider）摄取的发薪批次是否自动过账</summary>
    [RuntimeSetting(Label = "Auto-post External Runs", I18n = "admin.modules.system.settings.fields.externalAutoPost",
        Type = SettingFieldType.Boolean,
        Description = "When enabled, pay runs ingested from an external payroll provider are posted automatically.")]
    public bool ExternalAutoPost { get; set; } = true;
}
