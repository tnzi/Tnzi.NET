using System.Text.RegularExpressions;
using Tnzi.DependencyInjection;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Infers semantic column types from result column names using naming-convention heuristics.
/// Ported from Fabrikam's <c>SqlColumnInferrer</c> — patterns cover PascalCase and snake_case.
/// </summary>
public sealed partial class HeuristicSqlColumnInferrer : ISqlColumnInferrer, ISingletonDependency
{
    // ─── Inferred Type Constants ──────────────────────────────────────────────

    private const string TypeDate = "date";
    private const string TypeBoolean = "boolean";
    private const string TypeCurrency = "currency";
    private const string TypeNumber = "number";
    private const string TypePercent = "percent";
    private const string TypeBadge = "badge";
    private const string TypeString = "string";

    // ─── Interface ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<QueryColumn> Infer(string sql, IReadOnlyList<string> resultColumnNames)
    {
        Check.NotNull(sql);
        Check.NotNull(resultColumnNames);

        return resultColumnNames
            .Select(name => new QueryColumn(name, InferType(name)))
            .ToList();
    }

    // ─── Core Type Inference ──────────────────────────────────────────────────

    private static string InferType(string columnName)
    {
        if (IsDateColumn(columnName)) return TypeDate;
        if (IsBooleanColumn(columnName)) return TypeBoolean;
        if (IsCurrencyColumn(columnName)) return TypeCurrency;
        if (IsPercentColumn(columnName)) return TypePercent;
        if (IsNumberColumn(columnName)) return TypeNumber;
        if (IsBadgeColumn(columnName)) return TypeBadge;
        return TypeString;
    }

    // ─── Pattern Matchers ─────────────────────────────────────────────────────

    private static bool IsDateColumn(string name) =>
        DatePattern().IsMatch(name);

    private static bool IsBooleanColumn(string name) =>
        BooleanPattern().IsMatch(name);

    private static bool IsCurrencyColumn(string name) =>
        CurrencyPattern().IsMatch(name);

    private static bool IsPercentColumn(string name) =>
        name.Contains("Percent", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("percent", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Margin", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("Pct", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("_pct", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("%", StringComparison.Ordinal);

    private static bool IsNumberColumn(string name) =>
        NumberPattern().IsMatch(name);

    private static bool IsBadgeColumn(string name) =>
        BadgePattern().IsMatch(name);

    // ─── Generated Regex Patterns ─────────────────────────────────────────────

    /// <summary>
    /// Matches PascalCase and snake_case date/time column names.
    /// Examples: CreatedAt, created_at, StartDate, start_date, ExpiryTime, expiry_time
    /// </summary>
    [GeneratedRegex(
        @"(Date|Time|At|Since|Expiry|Period|PeriodStart|PeriodEnd|DueDate|PaidDate|StartDate|EndDate|HiredDate|CreatedAt|CreationTime|_at|_date|_time|_since)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex DatePattern();

    /// <summary>
    /// Matches boolean column names starting with "Is" / "Has" / "Can" or snake_case "is_" / "has_".
    /// Examples: IsActive, is_active, HasChildren, has_children
    /// </summary>
    [GeneratedRegex(
        @"^(Is[A-Z_]|Has[A-Z_]|Can[A-Z_]|is_|has_|can_)",
        RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex BooleanPattern();

    /// <summary>
    /// Matches currency/financial column names by suffix.
    /// Examples: TotalAmount, BasePay, CommissionFee, GrossRevenue
    /// </summary>
    [GeneratedRegex(
        @"(Amount|Total|Revenue|Profit|Pay|Fee|Commission|Gross|Net|Salary|Rate|Cost|Price|Spend|Expenses|Balance|Credit|Debit|Cpp|Ei|Tax|Gst|Hst|Wsib|Deduction|Invoice)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex CurrencyPattern();

    /// <summary>
    /// Matches count/quantity column names by suffix.
    /// Examples: ShiftCount, TotalHours, JobCount
    /// </summary>
    [GeneratedRegex(
        @"(Count|Shifts|Hours|Jobs|Invoices|Payments|Rating|Quantity|Number|Quota|Months|Days|_count|_hours|_days)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex NumberPattern();

    /// <summary>
    /// Matches status/category enum column names.
    /// Examples: Status, CandidateType, PaymentMethod, IsVoid
    /// </summary>
    [GeneratedRegex(
        @"(Status|Type|Method|Category|Mode|Reason|Tier|Level|IsVoid|_status|_type)$",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex BadgePattern();
}
