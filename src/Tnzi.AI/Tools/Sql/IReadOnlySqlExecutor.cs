namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Executes SELECT-only SQL against a configured read-only connection.
/// Enforces validation, row limits, timeout, and a mandatory permission check.
/// </summary>
public interface IReadOnlySqlExecutor
{
    Task<QueryResult> ExecuteAsync(
        string sql,
        ReadOnlySqlExecutionOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// Per-call execution options. Values left null/0 fall back to <see cref="SqlToolOptions"/>
/// defaults; supplied values are clamped to the corresponding <c>MaxAllowed*</c> ceilings.
/// </summary>
public sealed record ReadOnlySqlExecutionOptions(
    int? MaxRows = null,
    int? TimeoutSeconds = null,
    string? ConnectionName = null,
    SqlDialect? Dialect = null);
