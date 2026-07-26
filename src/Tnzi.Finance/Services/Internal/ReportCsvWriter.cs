namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 财务报表 CSV 生成器（内部共享）：复用报表 DTO，六报表 + 税务汇总统一出口
/// </summary>
/// <remarks>
/// 单元格转义与格式化统一委托核心 <see cref="CsvBuilder"/>（invariant culture、
/// RFC 4180 引号转义、公式注入防护）；日期按 yyyy-MM-dd 输出（记账日为 date-only 语义）。
/// </remarks>
internal static class ReportCsvWriter
{
    private const string DateFormat = "yyyy-MM-dd";

    internal static string TrialBalance(TrialBalanceReportDto report)
    {
        var csv = new CsvBuilder(DateFormat);
        csv.AppendRow("Code", "Name", "RootType", "OpeningBalance", "PeriodDebit", "PeriodCredit", "ClosingBalance");
        foreach (var row in report.Rows)
            csv.AppendRow(row.Code, row.Name, row.RootType, row.OpeningBalance, row.PeriodDebit, row.PeriodCredit, row.ClosingBalance);
        csv.AppendRow("Total", null, null, report.TotalOpeningBalance, report.TotalPeriodDebit, report.TotalPeriodCredit, report.TotalClosingBalance);
        return csv.ToString();
    }

    internal static string BalanceSheet(BalanceSheetReportDto report)
    {
        var csv = new CsvBuilder(DateFormat);
        csv.AppendRow("Section", "Code", "Name", "SubType", "Balance");
        AppendSection(csv, "Assets", report.Assets);
        csv.AppendRow("Assets", null, "Total assets", null, report.TotalAssets);
        AppendSection(csv, "Liabilities", report.Liabilities);
        csv.AppendRow("Liabilities", null, "Total liabilities", null, report.TotalLiabilities);
        AppendSection(csv, "Equity", report.Equity);
        csv.AppendRow("Equity", null, "Current earnings", null, report.CurrentEarnings);
        csv.AppendRow("Equity", null, "Total equity", null, report.TotalEquity);
        csv.AppendRow(null, null, "Balance check", null, report.BalanceCheck);
        return csv.ToString();
    }

    internal static string ProfitAndLoss(ProfitAndLossReportDto report)
    {
        var csv = new CsvBuilder(DateFormat);
        csv.AppendRow("Section", "Code", "Name", "SubType", "Balance");
        AppendSection(csv, "Income", report.Income);
        csv.AppendRow("Income", null, "Total income", null, report.TotalIncome);
        AppendSection(csv, "Expenses", report.Expenses);
        csv.AppendRow("Expenses", null, "Total expenses", null, report.TotalExpenses);
        csv.AppendRow(null, null, "Net profit", null, report.NetProfit);
        return csv.ToString();
    }

    /// <summary>
    /// 总账明细（全量行 + 期初余额行；最后一行的运行余额即期末余额）
    /// </summary>
    internal static string GeneralLedger(GeneralLedgerReportDto header, List<GeneralLedgerLineDto> lines)
    {
        var csv = new CsvBuilder(DateFormat);
        csv.AppendRow("EntryNumber", "PostingDate", "Memo", "SourceType", "SourceId", "PartyType", "PartyId", "Debit", "Credit", "RunningBalance");
        csv.AppendRow(null, header.From, "Opening balance", null, null, null, null, null, null, header.OpeningBalance);
        foreach (var line in lines)
            csv.AppendRow(line.EntryNumber, line.PostingDate, line.Memo, line.SourceType, line.SourceId, line.PartyType, line.PartyId, line.Debit, line.Credit, line.RunningBalance);
        return csv.ToString();
    }

    /// <summary>
    /// 账龄导出。表头随生效的切分点生成：桶已由 <c>Finance:AgingBucketDays</c> 参数化，
    /// 写死 1To30/31To60/61To90/Over90 会在配了 [7,14,21] 的部署里让列名与列里的数不符。
    /// 默认切分点 30/60/90 下生成的表头与旧版逐字一致。
    /// </summary>
    internal static string Aging(AgingReportDto report, int[] bucketDays)
    {
        var csv = new CsvBuilder(DateFormat);
        var (first, second, third) = (bucketDays[0], bucketDays[1], bucketDays[2]);
        csv.AppendRow("Party", "Current", $"Days1To{first}", $"Days{first + 1}To{second}",
            $"Days{second + 1}To{third}", $"Over{third}", "Total");
        foreach (var row in report.Rows)
            csv.AppendRow(row.PartyName, row.Current, row.Days1To30, row.Days31To60, row.Days61To90, row.Over90, row.Total);
        var t = report.Totals;
        csv.AppendRow("Total", t.Current, t.Days1To30, t.Days31To60, t.Days61To90, t.Over90, t.Total);
        return csv.ToString();
    }

    internal static string CashFlow(CashFlowReportDto report)
    {
        var csv = new CsvBuilder(DateFormat);
        csv.AppendRow("Section", "Code", "Name", "SubType", "Amount");
        csv.AppendRow("Operating", null, "Net profit", null, report.NetProfit);
        AppendSection(csv, "Operating", report.Operating);
        csv.AppendRow("Operating", null, "Net cash from operating activities", null, report.TotalOperating);
        AppendSection(csv, "Investing", report.Investing);
        csv.AppendRow("Investing", null, "Net cash from investing activities", null, report.TotalInvesting);
        AppendSection(csv, "Financing", report.Financing);
        csv.AppendRow("Financing", null, "Net cash from financing activities", null, report.TotalFinancing);
        if (report.Unclassified.Count > 0)
        {
            AppendSection(csv, "Unclassified", report.Unclassified);
            csv.AppendRow("Unclassified", null, "Net cash from unclassified accounts", null, report.TotalUnclassified);
        }

        csv.AppendRow(null, null, "Net cash flow", null, report.NetCashFlow);
        csv.AppendRow(null, null, "Opening cash", null, report.OpeningCash);
        csv.AppendRow(null, null, "Closing cash", null, report.ClosingCash);
        csv.AppendRow(null, null, "Check", null, report.CheckDifference);
        return csv.ToString();
    }

    internal static string TaxSummary(TaxSummaryReportDto report)
    {
        var csv = new CsvBuilder(DateFormat);
        csv.AppendRow("Agency", "RateName", "Rate", "OutputTax", "InputTax", "NetTax");
        foreach (var row in report.Rows)
            csv.AppendRow(row.AgencyName, row.RateName, row.Rate, row.OutputTax, row.InputTax, row.NetTax);
        csv.AppendRow("Total", null, null, report.TotalOutputTax, report.TotalInputTax, report.TotalNetTax);
        return csv.ToString();
    }

    private static void AppendSection(CsvBuilder csv, string section, List<ReportAccountRowDto> rows)
    {
        foreach (var row in rows)
            csv.AppendRow(section, row.Code, row.Name, row.SubType, row.Balance);
    }
}
