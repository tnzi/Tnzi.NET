using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Inspects database schema (tables, columns, distinct values) for AI tools
/// that need to describe the query surface without running arbitrary SQL.
/// </summary>
public interface ISchemaInspector
{
    Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ColumnInfo>> ListColumnsAsync(string tableName, CancellationToken ct = default);
    Task<IReadOnlyList<object?>> ListDistinctValuesAsync(string tableName, string columnName, int limit = 100, CancellationToken ct = default);
}

public sealed record TableInfo(string Schema, string Name, string? Comment);
public sealed record ColumnInfo(string Name, string DataType, bool Nullable, string? Comment);
