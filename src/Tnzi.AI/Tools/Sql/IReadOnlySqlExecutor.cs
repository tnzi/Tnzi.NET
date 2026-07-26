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

    /// <summary>
    /// Executes like <see cref="ExecuteAsync"/> but converts validation /
    /// permission / execution failures into a failed <see cref="QueryResult"/>
    /// (IsSuccess=false + ErrorMessage) instead of throwing - so callers can
    /// distinguish "no rows" from "rejected / timed out" and feed the error
    /// back to an AI to self-correct. Cancellation still propagates.
    /// </summary>
    async Task<QueryResult> TryExecuteAsync(
        string sql,
        ReadOnlySqlExecutionOptions? options = null,
        CancellationToken ct = default)
    {
        try
        {
            return await ExecuteAsync(sql, options, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return QueryResult.Failure(ex.Message, sql);
        }
    }
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
