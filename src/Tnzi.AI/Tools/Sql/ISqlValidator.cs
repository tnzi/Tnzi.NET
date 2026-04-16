namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Validates that a SQL statement is safe to execute under a read-only policy.
/// Implementations should reject non-SELECT statements, multi-statement queries,
/// and any forbidden keywords (DML/DDL).
/// </summary>
public interface ISqlValidator
{
    SqlValidationResult Validate(string sql);
}

public sealed record SqlValidationResult(bool IsValid, string? ErrorMessage);
