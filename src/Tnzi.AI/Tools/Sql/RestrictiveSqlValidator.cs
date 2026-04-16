using System;
using System.Linq;
using Tnzi.DependencyInjection;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Default <see cref="ISqlValidator"/> that allows only SELECT / WITH (CTE) statements,
/// rejects multi-statement queries, and blocks forbidden keywords (DML/DDL/EXEC/etc).
/// </summary>
public sealed class RestrictiveSqlValidator : ISqlValidator, ISingletonDependency
{
    private static readonly string[] ForbiddenKeywords =
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "GRANT", "REVOKE", "EXEC", "EXECUTE", "MERGE", "CALL"
    };

    public SqlValidationResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new SqlValidationResult(false, "SQL is empty");

        var trimmed = sql.Trim();
        var upper = trimmed.ToUpperInvariant();

        // Must start with SELECT or WITH (CTE)
        if (!upper.StartsWith("SELECT") && !upper.StartsWith("WITH "))
            return new SqlValidationResult(false, $"Only SELECT/WITH allowed; got: {trimmed.Substring(0, Math.Min(20, trimmed.Length))}");

        // Reject multi-statement: a semicolon is OK only at the very end
        var trimEnd = trimmed.TrimEnd();
        var idx = trimEnd.IndexOf(';');
        if (idx >= 0 && idx < trimEnd.Length - 1)
            return new SqlValidationResult(false, "Multi-statement SQL rejected");

        // Reject forbidden keywords as whole words
        var tokens = upper.Split(new[] { ' ', '\t', '\n', '\r', '(', ')', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var hit = tokens.FirstOrDefault(t => ForbiddenKeywords.Contains(t));
        if (hit is not null)
            return new SqlValidationResult(false, $"Forbidden keyword: {hit}");

        return new SqlValidationResult(true, null);
    }
}
