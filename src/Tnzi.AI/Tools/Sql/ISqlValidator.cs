namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Validates that a SQL statement is safe to execute under a read-only policy.
/// Implementations must reject non-SELECT statements, multi-statement queries,
/// and dangerous functions/procedures (xp_cmdshell, OPENROWSET, pg_read_file, load_file, ...).
/// </summary>
public interface ISqlValidator
{
    /// <summary>
    /// Validates <paramref name="sql"/> against the supplied dialect.
    /// </summary>
    /// <param name="sql">The SQL text to validate.</param>
    /// <param name="dialect">SQL dialect — selects the parsing strategy (AST for T-SQL, tokenizer for others).</param>
    SqlValidationResult Validate(string sql, SqlDialect dialect = SqlDialect.TSql);
}

public sealed record SqlValidationResult(bool IsValid, string? ErrorMessage);
