using System.Text.RegularExpressions;
using Tnzi.DependencyInjection;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Inspects database schema via ANSI <c>information_schema</c> views, using
/// <see cref="IReadOnlySqlExecutor"/> so all queries go through the same
/// validation / permission / read-only pipeline.
/// </summary>
public sealed partial class SchemaInspector : ISchemaInspector, IScopedDependency
{
    private readonly IReadOnlySqlExecutor _executor;

    // Only allow safe SQL identifier characters to prevent identifier injection
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex SafeIdentifierRegex();

    public SchemaInspector(IReadOnlySqlExecutor executor)
    {
        _executor = Check.NotNull(executor);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT table_schema, table_name, NULL AS comment
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
            ORDER BY table_schema, table_name
            """;

        var result = await _executor.ExecuteAsync(sql, ct: ct);

        return result.Rows
            .Select(row => new TableInfo(
                Schema: row[0]?.ToString() ?? string.Empty,
                Name: row[1]?.ToString() ?? string.Empty,
                Comment: row[2]?.ToString()))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ColumnInfo>> ListColumnsAsync(string tableName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        EnsureSafeIdentifier(tableName);

        // Use inline value — tableName is sanitized by regex; parameterized queries aren't
        // possible through the generic IReadOnlySqlExecutor text-only contract.
        var sql = $"""
            SELECT column_name, data_type, is_nullable, NULL AS comment
            FROM information_schema.columns
            WHERE table_name = '{EscapeLiteral(tableName)}'
            ORDER BY ordinal_position
            """;

        var result = await _executor.ExecuteAsync(sql, ct: ct);

        return result.Rows
            .Select(row => new ColumnInfo(
                Name: row[0]?.ToString() ?? string.Empty,
                DataType: row[1]?.ToString() ?? string.Empty,
                Nullable: string.Equals(row[2]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase),
                Comment: row[3]?.ToString()))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<object?>> ListDistinctValuesAsync(
        string tableName,
        string columnName,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        EnsureSafeIdentifier(tableName);
        EnsureSafeIdentifier(columnName);

        // Both identifiers are regex-validated; quote them for correctness
        var sql = $"""
            SELECT DISTINCT "{columnName}"
            FROM "{tableName}"
            LIMIT {limit}
            """;

        var result = await _executor.ExecuteAsync(sql, ct: ct);

        return result.Rows
            .Select(row => row.Count > 0 ? row[0] : null)
            .ToList();
    }

    private static void EnsureSafeIdentifier(string identifier)
    {
        if (!SafeIdentifierRegex().IsMatch(identifier))
            throw new ArgumentException(
                $"Invalid identifier '{identifier}'. Only letters, digits, and underscores allowed.",
                nameof(identifier));
    }

    /// <summary>
    /// Escapes a single-quote in a SQL string literal (for the WHERE clause).
    /// Already protected by <see cref="SafeIdentifierRegex"/> — this is belt-and-suspenders.
    /// </summary>
    private static string EscapeLiteral(string value) => value.Replace("'", "''");
}
