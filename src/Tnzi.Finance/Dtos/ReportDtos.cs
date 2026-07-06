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
}
