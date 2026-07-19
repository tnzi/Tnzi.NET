using System.Text;

namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// CSV 对账单解析器（按 <see cref="CsvMappingDto"/> 列映射，纯函数无状态）
/// </summary>
/// <remarks>
/// 列索引 0 基。金额支持单列带符号（AmountColumn）或双列（DebitColumn 取出款、CreditColumn 取入款，
/// Amount = Credit − Debit）。DecimalSeparator=","（欧洲格式）时先去千分位再归一化小数点。
/// 去重键（csv hash）由服务据 (accountId|date|amount|desc|序号) 计算，解析器不产出 ExternalId。
/// </remarks>
internal static class CsvStatementParser
{
    /// <summary>解析 CSV 文本；映射非法或无有效行时抛 <see cref="BusinessException"/>（400）。</summary>
    public static BankStatementParseResult Parse(string content, CsvMappingDto mapping)
    {
        Check.NotNull(mapping);
        if (string.IsNullOrWhiteSpace(content))
            throw new BusinessException("The CSV file is empty.");
        if (mapping.AmountColumn == null && mapping.DebitColumn == null && mapping.CreditColumn == null)
            throw new BusinessException("The CSV mapping must specify an amount column, or a debit/credit column pair.");

        var delimiter = string.IsNullOrEmpty(mapping.Delimiter) ? ',' : mapping.Delimiter[0];
        var records = ParseRecords(content, delimiter);

        var skip = Math.Max(0, mapping.SkipRows) + (mapping.HasHeader ? 1 : 0);
        var result = new BankStatementParseResult { Currency = NormalizeCurrency(mapping.Currency) };

        for (var i = skip; i < records.Count; i++)
        {
            var fields = records[i];
            if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace))
                continue;

            var date = ParseDate(GetField(fields, mapping.DateColumn), mapping.DateFormat);
            if (date == null)
                continue;

            decimal? amount;
            if (mapping.AmountColumn != null)
            {
                amount = ParseAmount(GetField(fields, mapping.AmountColumn.Value), mapping.DecimalSeparator);
            }
            else
            {
                var debit = ParseAmount(GetField(fields, mapping.DebitColumn), mapping.DecimalSeparator) ?? 0m;
                var credit = ParseAmount(GetField(fields, mapping.CreditColumn), mapping.DecimalSeparator) ?? 0m;
                amount = credit - debit;
            }

            if (amount == null)
                continue;

            result.Transactions.Add(new ParsedBankTransaction(
                PostedDate: date.Value,
                Amount: amount.Value,
                Currency: result.Currency,
                ExternalId: null,
                Description: GetField(fields, mapping.DescriptionColumn),
                Payee: null,
                Reference: GetField(fields, mapping.ReferenceColumn)));
        }

        if (result.Transactions.Count == 0)
            throw new BusinessException("No transactions could be parsed from the CSV with the provided mapping.");

        return result;
    }

    private static string? GetField(IReadOnlyList<string> fields, int? index)
    {
        if (index == null || index.Value < 0 || index.Value >= fields.Count)
            return null;
        var value = fields[index.Value].Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static DateTime? ParseDate(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!string.IsNullOrWhiteSpace(format) &&
            DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact.ToUtcDate();
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose)
            ? loose.ToUtcDate()
            : null;
    }

    private static decimal? ParseAmount(string? value, string? decimalSeparator)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().Replace("$", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (decimalSeparator == ",")
            normalized = normalized.Replace(".", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
        else
            normalized = normalized.Replace(",", "", StringComparison.Ordinal);

        // 括号记负（会计格式）
        var negative = false;
        if (normalized.StartsWith('(') && normalized.EndsWith(')'))
        {
            negative = true;
            normalized = normalized[1..^1];
        }

        if (!decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return null;
        return negative ? -parsed : parsed;
    }

    private static string? NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();

    /// <summary>RFC 4180 记录读取（支持双引号包裹、字段内引号转义、字段内换行）</summary>
    private static List<List<string>> ParseRecords(string content, char delimiter)
    {
        var records = new List<List<string>>();
        var current = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                current.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                    i++;
                current.Add(field.ToString());
                field.Clear();
                records.Add(current);
                current = new List<string>();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            records.Add(current);
        }

        return records;
    }
}
