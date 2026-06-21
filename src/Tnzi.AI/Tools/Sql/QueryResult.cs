namespace Tnzi.AI.Tools.Sql;

public sealed record QueryResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated,
    long DurationMs,
    string ExecutedSql,
    bool IsSuccess = true,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Builds a failed result carrying the error, so callers (e.g. AI text-to-SQL
    /// tools) can distinguish "no rows" from "rejected / timed out" instead of
    /// seeing an empty success.
    /// </summary>
    public static QueryResult Failure(string errorMessage, string executedSql, long durationMs = 0) =>
        new(
            Array.Empty<QueryColumn>(),
            Array.Empty<IReadOnlyList<object?>>(),
            Truncated: false,
            DurationMs: durationMs,
            ExecutedSql: executedSql,
            IsSuccess: false,
            ErrorMessage: errorMessage);
}

public sealed record QueryColumn(string Name, string InferredType);
