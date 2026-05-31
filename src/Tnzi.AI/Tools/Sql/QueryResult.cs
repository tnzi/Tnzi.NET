namespace Tnzi.AI.Tools.Sql;

public sealed record QueryResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated,
    long DurationMs,
    string ExecutedSql);

public sealed record QueryColumn(string Name, string InferredType);
