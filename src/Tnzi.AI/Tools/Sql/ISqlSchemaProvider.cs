namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Dialect-specific SQL builder for schema introspection. Encapsulates differences in
/// system catalog tables (information_schema vs sqlite_master vs ALL_TABLES) and
/// quoting / row-limit syntax that vary across providers. Identifier values are
/// validated by the caller before being passed in.
/// </summary>
public interface ISqlSchemaProvider
{
    /// <summary>The dialect this provider serves.</summary>
    SqlDialect Dialect { get; }

    /// <summary>SELECT for "list all base tables in the database (or current schema)".</summary>
    string ListTablesQuery();

    /// <summary>
    /// SELECT for "list columns of a single table". <paramref name="tableName"/> is
    /// expected to already be safety-validated by the caller; the provider applies
    /// its dialect-specific quoting / escaping.
    /// </summary>
    string ListColumnsQuery(string tableName);

    /// <summary>SELECT for "distinct values of one column up to N rows".</summary>
    string ListDistinctValuesQuery(string tableName, string columnName, int limit);
}
