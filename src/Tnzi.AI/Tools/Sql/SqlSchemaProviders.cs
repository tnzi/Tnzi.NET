using System.Text.RegularExpressions;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Shared helpers for <see cref="ISqlSchemaProvider"/> implementations.
/// Identifier quoting (and any future dialect-specific escape rules) live here
/// so all four built-in providers stay consistent.
/// </summary>
internal static partial class SqlSchemaProviderHelpers
{
    // 与 SchemaInspector.SafeIdentifierRegex 同款白名单，provider 内做深度防御，不依赖调用方校验。
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex SafeIdentifierRegex();

    /// <summary>
    /// Escapes a single quote for inclusion in a SQL string literal. Callers MUST also
    /// have validated the input through an identifier-safe regex first; this is the
    /// secondary belt-and-suspenders layer.
    /// </summary>
    public static string EscapeLiteral(string s) => s.Replace("'", "''");

    /// <summary>
    /// Validates that <paramref name="identifier"/> contains only safe identifier characters
    /// (letters, digits, underscore; not starting with a digit). Identifiers are interpolated
    /// directly into quoted SQL, so this is an in-provider defense-in-depth check independent
    /// of any caller-side validation. Throws <see cref="ArgumentException"/> when unsafe.
    /// </summary>
    public static string EnsureSafeIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || !SafeIdentifierRegex().IsMatch(identifier))
            throw new ArgumentException(
                $"Invalid SQL identifier '{identifier}'. Only letters, digits, and underscores allowed.",
                nameof(identifier));
        return identifier;
    }
}

/// <summary>
/// SQL Server (T-SQL) schema provider. Uses the ANSI <c>INFORMATION_SCHEMA</c> views
/// (available since SQL Server 2000), <c>[bracket]</c> identifier quoting, and
/// <c>SELECT TOP N</c> for row limits (T-SQL has no <c>LIMIT</c> clause).
/// </summary>
public sealed class TSqlSchemaProvider : ISqlSchemaProvider
{
    public SqlDialect Dialect => SqlDialect.TSql;

    public string ListTablesQuery() => """
        SELECT TABLE_SCHEMA, TABLE_NAME, NULL AS comment
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE'
        ORDER BY TABLE_SCHEMA, TABLE_NAME
        """;

    public string ListColumnsQuery(string tableName) => $"""
        SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, NULL AS comment
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = '{SqlSchemaProviderHelpers.EscapeLiteral(SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName))}'
          AND TABLE_SCHEMA = SCHEMA_NAME()
        ORDER BY ORDINAL_POSITION
        """;

    public string ListDistinctValuesQuery(string tableName, string columnName, int limit) => $"""
        SELECT DISTINCT TOP {limit} [{SqlSchemaProviderHelpers.EnsureSafeIdentifier(columnName)}]
        FROM [{SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName)}]
        """;
}

/// <summary>
/// PostgreSQL schema provider. Uses <c>information_schema</c> (lowercased; PG defaults
/// table/column names to lower case), double-quoted identifiers, and <c>LIMIT n</c>.
/// </summary>
public sealed class PostgreSqlSchemaProvider : ISqlSchemaProvider
{
    public SqlDialect Dialect => SqlDialect.PostgreSql;

    public string ListTablesQuery() => """
        SELECT table_schema, table_name, NULL AS comment
        FROM information_schema.tables
        WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name
        """;

    public string ListColumnsQuery(string tableName) => $"""
        SELECT column_name, data_type, is_nullable, NULL AS comment
        FROM information_schema.columns
        WHERE table_name = '{SqlSchemaProviderHelpers.EscapeLiteral(SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName))}'
          AND table_schema = current_schema()
        ORDER BY ordinal_position
        """;

    public string ListDistinctValuesQuery(string tableName, string columnName, int limit) => $"""
        SELECT DISTINCT "{SqlSchemaProviderHelpers.EnsureSafeIdentifier(columnName)}"
        FROM "{SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName)}"
        LIMIT {limit}
        """;
}

/// <summary>
/// MySQL schema provider. Uses <c>information_schema</c>, backtick-quoted identifiers
/// (the MySQL default; ANSI_QUOTES mode is not assumed), and <c>LIMIT n</c>.
/// </summary>
public sealed class MySqlSchemaProvider : ISqlSchemaProvider
{
    public SqlDialect Dialect => SqlDialect.MySql;

    public string ListTablesQuery() => """
        SELECT TABLE_SCHEMA, TABLE_NAME, NULL AS comment
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA NOT IN ('mysql', 'information_schema', 'performance_schema', 'sys')
        ORDER BY TABLE_SCHEMA, TABLE_NAME
        """;

    public string ListColumnsQuery(string tableName) => $"""
        SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_COMMENT AS comment
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = '{SqlSchemaProviderHelpers.EscapeLiteral(SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName))}'
          AND TABLE_SCHEMA = DATABASE()
        ORDER BY ORDINAL_POSITION
        """;

    public string ListDistinctValuesQuery(string tableName, string columnName, int limit) => $"""
        SELECT DISTINCT `{SqlSchemaProviderHelpers.EnsureSafeIdentifier(columnName)}`
        FROM `{SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName)}`
        LIMIT {limit}
        """;
}

/// <summary>
/// SQLite schema provider. Uses <c>sqlite_master</c> (no information_schema in SQLite),
/// double-quoted identifiers, and <c>LIMIT n</c>. Column metadata is queried via the
/// <c>pragma_table_info</c> table-valued function (SQLite 3.16.0+).
/// </summary>
public sealed class SqliteSchemaProvider : ISqlSchemaProvider
{
    public SqlDialect Dialect => SqlDialect.Sqlite;

    public string ListTablesQuery() => """
        SELECT 'main' AS table_schema, name AS table_name, NULL AS comment
        FROM sqlite_master
        WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
        ORDER BY name
        """;

    public string ListColumnsQuery(string tableName) => $"""
        SELECT name AS column_name, type AS data_type,
               CASE "notnull" WHEN 0 THEN 'YES' ELSE 'NO' END AS is_nullable,
               NULL AS comment
        FROM pragma_table_info('{SqlSchemaProviderHelpers.EscapeLiteral(SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName))}')
        ORDER BY cid
        """;

    public string ListDistinctValuesQuery(string tableName, string columnName, int limit) => $"""
        SELECT DISTINCT "{SqlSchemaProviderHelpers.EnsureSafeIdentifier(columnName)}"
        FROM "{SqlSchemaProviderHelpers.EnsureSafeIdentifier(tableName)}"
        LIMIT {limit}
        """;
}
