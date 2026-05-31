namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Infers semantic column types from result column names using naming-convention heuristics.
/// Built-in patterns cover generic English Pascal/snake-case names; domain-specific terms
/// (e.g. payroll abbreviations) should be supplied by the application via
/// <see cref="ISqlColumnInferenceRule"/> implementations registered in DI.
/// </summary>
public sealed partial class HeuristicSqlColumnInferrer : ISqlColumnInferrer
{
    private const string TypeDate = "date";
    private const string TypeBoolean = "boolean";
    private const string TypeCurrency = "currency";
    private const string TypeNumber = "number";
    private const string TypePercent = "percent";
    private const string TypeBadge = "badge";
    private const string TypeString = "string";

    private readonly IReadOnlyList<ISqlColumnInferenceRule> _customRules;

    public HeuristicSqlColumnInferrer(IEnumerable<ISqlColumnInferenceRule>? customRules = null)
    {
        _customRules = customRules?.ToArray() ?? Array.Empty<ISqlColumnInferenceRule>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<QueryColumn> Infer(string sql, IReadOnlyList<string> resultColumnNames)
    {
        Check.NotNull(sql);
        Check.NotNull(resultColumnNames);

        return resultColumnNames
            .Select(name => new QueryColumn(name, InferType(name)))
            .ToList();
    }

    private string InferType(string columnName)
    {
        // Application-supplied rules win — they encode domain knowledge the framework cannot.
        foreach (var rule in _customRules)
        {
            var custom = rule.TryInferType(columnName);
            if (!string.IsNullOrEmpty(custom)) return custom;
        }

        if (DatePattern().IsMatch(columnName)) return TypeDate;
        if (BooleanPattern().IsMatch(columnName)) return TypeBoolean;
        if (IsCurrencyColumn(columnName)) return TypeCurrency;
        if (IsPercentColumn(columnName)) return TypePercent;
        if (NumberPattern().IsMatch(columnName)) return TypeNumber;
        if (BadgePattern().IsMatch(columnName)) return TypeBadge;
        return TypeString;
    }

    private static bool IsCurrencyColumn(string name) => CurrencyPattern().IsMatch(name);

    private static bool IsPercentColumn(string name) =>
        name.Contains("Percent", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Margin", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("Pct", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("_pct", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("%", StringComparison.Ordinal);

    /// <summary>Date/time columns: PascalCase and snake_case suffixes.</summary>
    [GeneratedRegex(
        @"(Date|Time|At|Since|Expiry|Period|DueDate|PaidDate|StartDate|EndDate|HiredDate|CreatedAt|CreationTime|_at|_date|_time|_since)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex DatePattern();

    /// <summary>Boolean columns: Is*/Has*/Can* prefix or snake_case is_/has_/can_.</summary>
    [GeneratedRegex(
        @"^(Is[A-Z_]|Has[A-Z_]|Can[A-Z_]|is_|has_|can_)",
        RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex BooleanPattern();

    /// <summary>
    /// Currency/financial columns by suffix. Generic English terms only — domain abbreviations
    /// (CPP/EI/WSIB/HST/GST etc.) belong in an application-supplied <see cref="ISqlColumnInferenceRule"/>.
    /// </summary>
    [GeneratedRegex(
        @"(Amount|Total|Revenue|Profit|Pay|Fee|Commission|Gross|Net|Salary|Rate|Cost|Price|Spend|Expenses|Balance|Credit|Debit|Tax)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex CurrencyPattern();

    /// <summary>Count/quantity columns by suffix.</summary>
    [GeneratedRegex(
        @"(Count|Hours|Quantity|Number|Quota|Months|Days|_count|_hours|_days)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex NumberPattern();

    /// <summary>Status/category enum columns.</summary>
    [GeneratedRegex(
        @"(Status|Type|Method|Category|Mode|Reason|Tier|Level|_status|_type)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex BadgePattern();
}
