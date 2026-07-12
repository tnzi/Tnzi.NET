namespace Tnzi.Finance.Dtos;

/// <summary>
/// 试算平衡表
/// </summary>
public class TrialBalanceReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public List<TrialBalanceRowDto> Rows { get; set; } = new();
    public decimal TotalOpeningBalance { get; set; }
    public decimal TotalPeriodDebit { get; set; }
    public decimal TotalPeriodCredit { get; set; }
    public decimal TotalClosingBalance { get; set; }
}

/// <summary>
/// 试算平衡行（余额有符号：借方为正、贷方为负）
/// </summary>
public class TrialBalanceRowDto
{
    public Guid AccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountRootType RootType { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal ClosingBalance { get; set; }
}

/// <summary>
/// 报表科目行（余额按科目自然方向为正）
/// </summary>
public class ReportAccountRowDto
{
    public Guid AccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountRootType RootType { get; set; }
    public string? SubType { get; set; }
    public decimal Balance { get; set; }
}

/// <summary>
/// 资产负债表
/// </summary>
public class BalanceSheetReportDto
{
    public DateTime AsOf { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public List<ReportAccountRowDto> Assets { get; set; } = new();
    public List<ReportAccountRowDto> Liabilities { get; set; } = new();
    public List<ReportAccountRowDto> Equity { get; set; } = new();

    /// <summary>本年（累计）利润：收入与费用类科目自开账以来的净额（计算行，无需年末结转）</summary>
    public decimal CurrentEarnings { get; set; }

    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }

    /// <summary>权益合计（含 CurrentEarnings）</summary>
    public decimal TotalEquity { get; set; }

    /// <summary>配平校验差额（应为 0；TotalAssets - TotalLiabilities - TotalEquity）</summary>
    public decimal BalanceCheck { get; set; }
}

/// <summary>
/// 利润表
/// </summary>
public class ProfitAndLossReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public List<ReportAccountRowDto> Income { get; set; } = new();
    public List<ReportAccountRowDto> Expenses { get; set; } = new();
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetProfit { get; set; }
}

/// <summary>
/// 总账明细（单科目）
/// </summary>
public class GeneralLedgerReportDto
{
    public Guid AccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;

    /// <summary>期初余额（有符号：借方为正）</summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>期末余额（有符号：借方为正）</summary>
    public decimal ClosingBalance { get; set; }

    public IPagedList<GeneralLedgerLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 总账明细行
/// </summary>
public class GeneralLedgerLineDto
{
    public Guid JournalEntryId { get; set; }
    public string? EntryNumber { get; set; }
    public DateTime PostingDate { get; set; }
    public string? Memo { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? PartyType { get; set; }
    public string? PartyId { get; set; }

    /// <summary>来源单据类型（凭证头透传，供 register 场景回链业务单据）</summary>
    public string? SourceType { get; set; }

    /// <summary>来源单据ID（凭证头透传）</summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// 运行余额（有符号：借方为正；= 期初余额 + 截至本行的期间借贷净额。
    /// 跨页连续：第 N 页起点 = 期初余额 + 页首之前所有行的净额）
    /// </summary>
    public decimal RunningBalance { get; set; }
}

/// <summary>
/// 现金流量表（间接法）
/// </summary>
/// <remarks>
/// 从净利润出发，按科目 <see cref="CashFlowActivity"/> 分类聚合资产负债类科目的期间变动
/// （行金额为现金流视角：流入为正——资产减少/负债增加为流入）。
/// 未分类科目落入显式的 Unclassified 桶；现金及现金等价物按 CashEquivalent 分类识别。
/// CheckDifference 为恒等式校验行（沿资产负债表 BalanceCheck 先例）：
/// 只要每个科目恰好归入一个桶，净现金流必然等于现金科目 GL 变动，非 0 即聚合实现有误。
/// </remarks>
public class CashFlowReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;

    /// <summary>期间净利润（间接法起点）</summary>
    public decimal NetProfit { get; set; }

    /// <summary>经营活动调整行（应收/应付/存货等营运资本变动）</summary>
    public List<ReportAccountRowDto> Operating { get; set; } = new();

    /// <summary>投资活动行</summary>
    public List<ReportAccountRowDto> Investing { get; set; } = new();

    /// <summary>筹资活动行</summary>
    public List<ReportAccountRowDto> Financing { get; set; } = new();

    /// <summary>未分类科目行（CashFlowActivity 为 null 的资产负债类科目；提示用户去科目表补分类）</summary>
    public List<ReportAccountRowDto> Unclassified { get; set; } = new();

    /// <summary>经营活动现金流小计（= 净利润 + 经营调整行合计）</summary>
    public decimal TotalOperating { get; set; }

    public decimal TotalInvesting { get; set; }
    public decimal TotalFinancing { get; set; }
    public decimal TotalUnclassified { get; set; }

    /// <summary>净现金流（四个活动小计之和）</summary>
    public decimal NetCashFlow { get; set; }

    /// <summary>期初现金及现金等价物（CashEquivalent 科目期初余额，借方为正）</summary>
    public decimal OpeningCash { get; set; }

    /// <summary>期末现金及现金等价物</summary>
    public decimal ClosingCash { get; set; }

    /// <summary>现金净变动（期末 - 期初，即现金科目 GL 期间变动）</summary>
    public decimal CashMovement { get; set; }

    /// <summary>恒等式校验差额（NetCashFlow - CashMovement，应为 0）</summary>
    public decimal CheckDifference { get; set; }
}

/// <summary>
/// 税务申报汇总报表（期间 × 税务机构/税率维度的销项税、进项税与净额）
/// </summary>
/// <remarks>
/// 纯 GL 聚合：只统计已过账且携带 TaxRateId 的总账行，
/// 销项税 = TaxPayable 角色科目上的贷方净额，进项税 = TaxReceivable 角色科目上的借方净额。
/// 税维度自引入迁移起写入，历史行无税维度不计入（口径见 docs/modules/finance.md）。
/// </remarks>
public class TaxSummaryReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public List<TaxSummaryRowDto> Rows { get; set; } = new();
    public decimal TotalOutputTax { get; set; }
    public decimal TotalInputTax { get; set; }

    /// <summary>净额合计（销项 - 进项；正数为应缴）</summary>
    public decimal TotalNetTax { get; set; }
}

/// <summary>
/// 税务申报汇总行（每税率一行，按税务机构分组排序）
/// </summary>
public class TaxSummaryRowDto
{
    public Guid TaxRateId { get; set; }

    /// <summary>税率名称（税率已删除时为 null）</summary>
    public string? RateName { get; set; }

    /// <summary>税率百分比（税率已删除时为 null）</summary>
    public decimal? Rate { get; set; }

    public Guid? AgencyId { get; set; }
    public string? AgencyName { get; set; }

    /// <summary>销项税（TaxPayable 角色科目贷方净额）</summary>
    public decimal OutputTax { get; set; }

    /// <summary>进项税（TaxReceivable 角色科目借方净额）</summary>
    public decimal InputTax { get; set; }

    /// <summary>净额（销项 - 进项；正数为应缴）</summary>
    public decimal NetTax { get; set; }
}
