using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Inspects database schema by delegating SQL construction to a dialect-specific
/// <see cref="ISqlSchemaProvider"/> and routing execution through
/// <see cref="IReadOnlySqlExecutor"/> so all queries respect the same validation,
/// permission and read-only guarantees.
/// </summary>
public sealed partial class SchemaInspector : ISchemaInspector
{
    private readonly IReadOnlySqlExecutor _executor;
    private readonly IReadOnlyDictionary<SqlDialect, ISqlSchemaProvider> _providersByDialect;
    private readonly SqlToolOptions _options;
    private readonly ReadOnlySqlExecutionOptions _execOptions;

    // Only allow safe SQL identifier characters to prevent identifier injection.
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex SafeIdentifierRegex();

    public SchemaInspector(
        IReadOnlySqlExecutor executor,
        IEnumerable<ISqlSchemaProvider> providers,
        IOptions<SqlToolOptions> options)
    {
        _executor = Check.NotNull(executor);
        _options = Check.NotNull(options).Value;
        _execOptions = new ReadOnlySqlExecutionOptions(Dialect: _options.DefaultDialect);

        var providersList = (providers ?? Array.Empty<ISqlSchemaProvider>()).ToList();
        // Last registration wins for a given dialect - apps can override the built-in.
        _providersByDialect = providersList
            .GroupBy(p => p.Dialect)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct = default)
    {
        var provider = ResolveProvider();
        var result = await _executor.ExecuteAsync(provider.ListTablesQuery(), _execOptions, ct);

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

        var provider = ResolveProvider();
        var result = await _executor.ExecuteAsync(provider.ListColumnsQuery(tableName), _execOptions, ct);

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

        var provider = ResolveProvider();
        var sql = provider.ListDistinctValuesQuery(tableName, columnName, limit);
        var result = await _executor.ExecuteAsync(sql, _execOptions, ct);

        return result.Rows
            .Select(row => row.Count > 0 ? row[0] : null)
            .ToList();
    }

    private ISqlSchemaProvider ResolveProvider()
    {
        var dialect = _options.DefaultDialect;
        if (_providersByDialect.TryGetValue(dialect, out var provider))
            return provider;
        throw new InvalidOperationException(
            $"No ISqlSchemaProvider registered for dialect {dialect}. Register one via DI " +
            "(AIModule registers TSql/PostgreSQL/MySQL/SQLite by default).");
    }

    private static void EnsureSafeIdentifier(string identifier)
    {
        if (!SafeIdentifierRegex().IsMatch(identifier))
            throw new ArgumentException(
                $"Invalid identifier '{identifier}'. Only letters, digits, and underscores allowed.",
                nameof(identifier));
    }
}
