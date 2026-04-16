using System.Threading;
using System.Threading.Tasks;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Executes SELECT-only SQL against a configured read-only connection.
/// Enforces validation, row limits, timeout, and optional permission checks.
/// </summary>
public interface IReadOnlySqlExecutor
{
    Task<QueryResult> ExecuteAsync(
        string sql,
        ReadOnlySqlExecutionOptions? options = null,
        CancellationToken ct = default);
}

public sealed record ReadOnlySqlExecutionOptions(
    int MaxRows = 10000,
    int TimeoutSeconds = 30,
    string? ConnectionName = null);
