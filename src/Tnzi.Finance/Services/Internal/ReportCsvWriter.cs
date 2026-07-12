namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 财务报表 CSV 生成器（内部共享）：复用报表 DTO，六报表 + 税务汇总统一出口
/// </summary>
/// <remarks>
/// 输出约定：invariant culture（小数点固定 "."、日期 yyyy-MM-dd）；
/// 字符串单元格做 CSV 引号转义 + 公式注入转义（以 = + - @ 或制表符开头的值前置单引号，
/// 防止 Excel/Sheets 将单元格当公式执行）。数值/日期由类型化格式输出，不经公式转义。
/// </remarks>
internal static class ReportCsvWriter
{
    internal static string TrialBalance(TrialBalanceReportDto report)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "Code", "Name", "RootType", "OpeningBalance", "PeriodDebit", "PeriodCredit", "ClosingBalance");
        foreach (var row in report.Rows)
            AppendRow(sb, row.Code, row.Name, row.RootType, row.OpeningBalance, row.PeriodDebit, row.PeriodCredit, row.ClosingBalance);
        AppendRow(sb, "Total", null, null, report.TotalOpeningBalance, report.TotalPeriodDebit, report.TotalPeriodCredit, report.TotalClosingBalance);
        return sb.ToString();
    }

    internal static string BalanceSheet(BalanceSheetReportDto report)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "Section", "Code", "Name", "SubType", "Balance");
        AppendSection(sb, "Assets", report.Assets);
        AppendRow(sb, "Assets", null, "Total assets", null, report.TotalAssets);
        AppendSection(sb, "Liabilities", report.Liabilities);
        AppendRow(sb, "Liabilities", null, "Total liabilities", null, report.TotalLiabilities);
        AppendSection(sb, "Equity", report.Equity);
        AppendRow(sb, "Equity", null, "Current earnings", null, report.CurrentEarnings);
        AppendRow(sb, "Equity", null, "Total equity", null, report.TotalEquity);
        AppendRow(sb, null, null, "Balance check", null, report.BalanceCheck);
        return sb.ToString();
    }

    internal static string ProfitAndLoss(ProfitAndLossReportDto report)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "Section", "Code", "Name", "SubType", "Balance");
        AppendSection(sb, "Income", report.Income);
        AppendRow(sb, "Income", null, "Total income", null, report.TotalIncome);
        AppendSection(sb, "Expenses", report.Expenses);
        AppendRow(sb, "Expenses", null, "Total expenses", null, report.TotalExpenses);
        AppendRow(sb, null, null, "Net profit", null, report.NetProfit);
        return sb.ToString();
    }

    /// <summary>
    /// 总账明细（全量行 + 期初余额行；最后一行的运行余额即期末余额）
    /// </summary>
    internal static string GeneralLedger(GeneralLedgerReportDto header, List<GeneralLedgerLineDto> lines)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "EntryNumber", "PostingDate", "Memo", "SourceType", "SourceId", "PartyType", "PartyId", "Debit", "Credit", "RunningBalance");
        AppendRow(sb, null, header.From, "Opening balance", null, null, null, null, null, null, header.OpeningBalance);
        foreach (var line in lines)
            AppendRow(sb, line.EntryNumber, line.PostingDate, line.Memo, line.SourceType, line.SourceId, line.PartyType, line.PartyId, line.Debit, line.Credit, line.RunningBalance);
        return sb.ToString();
    }

    internal static string Aging(AgingReportDto report)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "Party", "Current", "Days1To30", "Days31To60", "Days61To90", "Over90", "Total");
        foreach (var row in report.Rows)
            AppendRow(sb, row.PartyName, row.Current, row.Days1To30, row.Days31To60, row.Days61To90, row.Over90, row.Total);
        var t = report.Totals;
        AppendRow(sb, "Total", t.Current, t.Days1To30, t.Days31To60, t.Days61To90, t.Over90, t.Total);
        return sb.ToString();
    }

    internal static string CashFlow(CashFlowReportDto report)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "Section", "Code", "Name", "SubType", "Amount");
        AppendRow(sb, "Operating", null, "Net profit", null, report.NetProfit);
        AppendSection(sb, "Operating", report.Operating);
        AppendRow(sb, "Operating", null, "Net cash from operating activities", null, report.TotalOperating);
        AppendSection(sb, "Investing", report.Investing);
        AppendRow(sb, "Investing", null, "Net cash from investing activities", null, report.TotalInvesting);
        AppendSection(sb, "Financing", report.Financing);
        AppendRow(sb, "Financing", null, "Net cash from financing activities", null, report.TotalFinancing);
        if (report.Unclassified.Count > 0)
        {
            AppendSection(sb, "Unclassified", report.Unclassified);
            AppendRow(sb, "Unclassified", null, "Net cash from unclassified accounts", null, report.TotalUnclassified);
        }

        AppendRow(sb, null, null, "Net cash flow", null, report.NetCashFlow);
        AppendRow(sb, null, null, "Opening cash", null, report.OpeningCash);
        AppendRow(sb, null, null, "Closing cash", null, report.ClosingCash);
        AppendRow(sb, null, null, "Check", null, report.CheckDifference);
        return sb.ToString();
    }

    internal static string TaxSummary(TaxSummaryReportDto report)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "Agency", "RateName", "Rate", "OutputTax", "InputTax", "NetTax");
        foreach (var row in report.Rows)
            AppendRow(sb, row.AgencyName, row.RateName, row.Rate, row.OutputTax, row.InputTax, row.NetTax);
        AppendRow(sb, "Total", null, null, report.TotalOutputTax, report.TotalInputTax, report.TotalNetTax);
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string section, List<ReportAccountRowDto> rows)
    {
        foreach (var row in rows)
            AppendRow(sb, section, row.Code, row.Name, row.SubType, row.Balance);
    }

    private static void AppendRow(StringBuilder sb, params object?[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(FormatCell(cells[i]));
        }

        sb.AppendLine();
    }

    private static string FormatCell(object? cell) => cell switch
    {
        null => string.Empty,
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s => Escape(s),
        _ => Escape(Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty)
    };

    private static string Escape(string value)
    {
        if (value.Length == 0)
            return value;

        // 公式注入转义：前置单引号使电子表格按文本解析
        if (value[0] is '=' or '+' or '-' or '@' or '\t')
            value = "'" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}
