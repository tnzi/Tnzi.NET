namespace Tnzi.Finance.Documents.Services;

/// <summary>
/// 对账单渲染：出一张可打印的 HTML
/// </summary>
/// <remarks>
/// 与支票同一取舍：产物是 HTML 而非 PDF，靠浏览器的 <c>@media print</c> 出纸——
/// 服务端因此零 PDF 引擎依赖，而对账单本来就常常是"贴进邮件正文"而不是当附件寄。
/// 需要 PDF 的部署自己注册一个实现即可。
///
/// 金额一律走会计惯例：负数用括号、货币符号在括号内、等宽数字对齐——与前端
/// <c>utils/finance-format.ts</c> 同一套规则，同一张单据在屏幕上和纸上读起来一样。
/// </remarks>
public class TemplateStatementRenderer : IStatementRenderer
{
    public string ContentType => "text/html";
    public string FileExtension => ".html";

    public Task<Result<byte[]>> RenderAsync(CustomerStatementDto statement, CancellationToken cancellationToken = default)
    {
        Check.NotNull(statement);

        var isOpenItem = statement.Style == StatementStyle.OpenItem;
        var sb = new StringBuilder();

        sb.Append("""
            <!doctype html><html><head><meta charset="utf-8">
            <title>Statement</title>
            <style>
              body { font-family: -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; color:#222; margin:32px; }
              h1 { font-size:20px; margin:0 0 2px; }
              .meta { color:#666; font-size:12px; margin-bottom:18px; }
              table { border-collapse:collapse; width:100%; font-size:13px; }
              th, td { padding:6px 8px; border-bottom:1px solid #e6e6e6; text-align:left; }
              th { font-size:11px; text-transform:uppercase; letter-spacing:.04em; color:#666; }
              .num { text-align:right; font-variant-numeric: tabular-nums lining-nums; white-space:nowrap; }
              .totals { margin-top:16px; display:flex; justify-content:flex-end; }
              .totals table { width:auto; }
              .totals td { border:0; padding:3px 8px; }
              .totals .grand { font-weight:700; border-top:2px solid #222; }
              .overdue { color:#b00; }
              .aging { margin-top:20px; font-size:12px; color:#444; }
              .aging td, .aging th { border:0; padding:3px 10px 3px 0; }
              @media print { body { margin:0; } }
            </style></head><body>
            """);

        sb.Append("<h1>").Append(Escape(statement.PartyName)).Append("</h1>");
        sb.Append("<div class=\"meta\">")
          .Append(isOpenItem ? "Statement of open items as at " : "Statement of activity ")
          .Append(isOpenItem
              ? FormatDate(statement.PeriodTo)
              : $"{FormatDate(statement.PeriodFrom)} to {FormatDate(statement.PeriodTo)}")
          .Append("</div>");

        sb.Append("<table><thead><tr>")
          .Append("<th>Date</th><th>Document</th><th>Due</th>")
          .Append("<th class=\"num\">Charges</th><th class=\"num\">Payments</th>")
          .Append(isOpenItem ? "<th class=\"num\">Outstanding</th>" : "<th class=\"num\">Balance</th>")
          .Append("</tr></thead><tbody>");

        if (!isOpenItem && statement.OpeningBalance != 0)
        {
            sb.Append("<tr><td>").Append(FormatDate(statement.PeriodFrom)).Append("</td>")
              .Append("<td><em>Opening balance</em></td><td></td><td class=\"num\"></td><td class=\"num\"></td>")
              .Append("<td class=\"num\">").Append(Money(statement.OpeningBalance, statement.Currency)).Append("</td></tr>");
        }

        foreach (var line in statement.Lines)
        {
            var overdue = line.OverdueDays > 0;
            sb.Append("<tr><td>").Append(FormatDate(line.DocDate)).Append("</td>")
              .Append("<td>").Append(Escape(line.Number ?? line.DocType)).Append("</td>")
              .Append("<td").Append(overdue ? " class=\"overdue\"" : string.Empty).Append('>')
              .Append(line.DueDate.HasValue ? FormatDate(line.DueDate.Value) : string.Empty)
              .Append(overdue ? $" ({line.OverdueDays}d)" : string.Empty)
              .Append("</td>")
              .Append("<td class=\"num\">").Append(line.Charge == 0 ? "" : Money(line.Charge, statement.Currency)).Append("</td>")
              .Append("<td class=\"num\">").Append(line.Payment == 0 ? "" : Money(line.Payment, statement.Currency)).Append("</td>")
              .Append("<td class=\"num\">").Append(Money(isOpenItem ? line.Outstanding : line.Balance, statement.Currency)).Append("</td>")
              .Append("</tr>");
        }

        sb.Append("</tbody></table>");

        sb.Append("<div class=\"totals\"><table>");
        if (statement.Overdue > 0)
        {
            sb.Append("<tr><td>Overdue</td><td class=\"num overdue\">")
              .Append(Money(statement.Overdue, statement.Currency)).Append("</td></tr>");
        }
        sb.Append("<tr class=\"grand\"><td>Amount due</td><td class=\"num\">")
          .Append(Money(statement.ClosingBalance, statement.Currency)).Append("</td></tr></table></div>");

        var b = statement.Buckets;
        sb.Append("<table class=\"aging\"><tr><th>Current</th><th>1-30</th><th>31-60</th><th>61-90</th><th>90+</th></tr><tr>")
          .Append("<td class=\"num\">").Append(Money(b.Current, statement.Currency)).Append("</td>")
          .Append("<td class=\"num\">").Append(Money(b.Days1To30, statement.Currency)).Append("</td>")
          .Append("<td class=\"num\">").Append(Money(b.Days31To60, statement.Currency)).Append("</td>")
          .Append("<td class=\"num\">").Append(Money(b.Days61To90, statement.Currency)).Append("</td>")
          .Append("<td class=\"num\">").Append(Money(b.Over90, statement.Currency)).Append("</td></tr></table>");

        sb.Append("</body></html>");

        return Task.FromResult(Result<byte[]>.Success(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// 会计惯例：负数用括号，货币符号在括号**内**。
    /// </summary>
    /// <remarks>
    /// 与前端 <c>formatMoney</c> 同一规则。减号在这行当里等于自报不专业。
    /// </remarks>
    private static string Money(decimal amount, string? currency)
    {
        var symbol = currency switch
        {
            "USD" or "CAD" or "AUD" or "NZD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "JPY" or "CNY" => "¥",
            _ => string.Empty,
        };
        var body = symbol + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);
        return amount < 0 ? $"({body})" : body;
    }

    /// <summary>
    /// 固定 <c>Jan 15, 2026</c>：美式 MM/DD/YYYY 与加拿大 YYYY-MM-DD 民间混用，
    /// 交给区域设置等于同一张纸两位同事读出不同日期。
    /// </summary>
    private static string FormatDate(DateTime value)
        => value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    private static string Escape(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : WebUtility.HtmlEncode(text);
}
