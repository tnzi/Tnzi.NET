using IDatabaseProvider = Tnzi.EFCore.Dapper.Providers.IDatabaseProvider;

namespace Tnzi.EFCore.Dapper;

/// <summary>
/// Dapper 批量操作工具类
/// </summary>
public static class DapperBulkOperations
{
    private const int DefaultBatchSize = 1000;

    /// <summary>
    /// 批量插入（使用数据库特定的批量插入语法）
    /// </summary>
    /// <param name="batchSize">每批处理的实体数量，默认 1000</param>
    public static async Task<int> BulkInsertAsync<T>(
        IDbConnection connection,
        IDatabaseProvider provider,
        DbContext dbContext,
        IEnumerable<T> entities,
        string? tableName = null,
        IDbTransaction? transaction = null,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default) where T : class
    {
        Check.NotNull(connection);
        Check.NotNull(provider);
        Check.NotNull(dbContext);
        Check.NotNull(entities);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return 0;

        // 获取表名
        tableName ??= DapperEntityHelper.GetTableName<T>(dbContext);
        SqlIdentifierHelper.ThrowIfInvalidIdentifier(tableName, nameof(tableName));
        var escapedTable = provider.EscapeIdentifier(tableName);

        // 获取列映射（排除主键，因为主键可能是自动生成的）
        var mappings = DapperEntityHelper.GetColumnMappings<T>(dbContext, excludeKey: true);
        if (mappings.Count == 0)
            throw new InvalidOperationException($"No properties found for type {typeof(T).Name}");

        // 验证所有列名
        foreach (var mapping in mappings)
        {
            SqlIdentifierHelper.ThrowIfInvalidIdentifier(mapping.ColumnName, "column");
        }

        // 预缓存 PropertyInfo
        var propertyInfos = CachePropertyInfos<T>(mappings);

        var totalInserted = 0;
        var batches = entityList.Chunk(batchSize > 0 ? batchSize : DefaultBatchSize);

        foreach (var batch in batches)
        {
            var batchList = batch.ToList();
            var columnNames = mappings.Select(m => m.ColumnName).ToList();
            var sql = GenerateBulkInsertSql(provider, escapedTable, columnNames, batchList.Count);

            // 准备参数：使用列名作为参数名基础（未转义的原始名称）
            var parameters = new DynamicParameters();
            for (int i = 0; i < batchList.Count; i++)
            {
                var entity = batchList[i];
                foreach (var mapping in mappings)
                {
                    if (propertyInfos.TryGetValue(mapping.PropertyName, out var prop))
                    {
                        var value = prop.GetValue(entity);
                        parameters.Add($"{mapping.ColumnName}_{i}", value);
                    }
                }
            }

            totalInserted += await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        }

        return totalInserted;
    }

    /// <summary>
    /// 批量更新（使用数据库特定的批量更新语法）
    /// </summary>
    /// <param name="batchSize">每批处理的实体数量，默认 1000</param>
    internal static async Task<int> BulkUpdateAsync<T>(
        IDbConnection connection,
        IDatabaseProvider provider,
        DbContext dbContext,
        IEnumerable<T> entities,
        string? tableName = null,
        string? keyColumn = null,
        IDbTransaction? transaction = null,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default) where T : class
    {
        Check.NotNull(connection);
        Check.NotNull(provider);
        Check.NotNull(dbContext);
        Check.NotNull(entities);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return 0;

        tableName ??= DapperEntityHelper.GetTableName<T>(dbContext);

        // 获取主键映射
        var keyMapping = DapperEntityHelper.GetKeyMapping(typeof(T), dbContext);
        keyColumn ??= keyMapping.ColumnName;

        SqlIdentifierHelper.ThrowIfInvalidIdentifier(tableName, nameof(tableName));
        SqlIdentifierHelper.ThrowIfInvalidIdentifier(keyColumn, nameof(keyColumn));

        // 获取列映射（排除主键）
        var mappings = DapperEntityHelper.GetColumnMappings<T>(dbContext, excludeKey: true);
        if (mappings.Count == 0)
            throw new InvalidOperationException($"No properties found for type {typeof(T).Name}");

        // 验证所有列名
        foreach (var mapping in mappings)
        {
            SqlIdentifierHelper.ThrowIfInvalidIdentifier(mapping.ColumnName, "column");
        }

        var escapedTable = provider.EscapeIdentifier(tableName);
        var escapedKey = provider.EscapeIdentifier(keyColumn);

        // 获取主键的 PropertyInfo（通过 CLR 属性名反射）
        var keyPropertyInfo = typeof(T).GetProperty(keyMapping.PropertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Cannot find key property '{keyMapping.PropertyName}' on type {typeof(T).Name}");

        // 预缓存 PropertyInfo
        var propertyInfos = CachePropertyInfos<T>(mappings);

        var columnNames = mappings.Select(m => m.ColumnName).ToList();
        var totalUpdated = 0;
        var batches = entityList.Chunk(batchSize > 0 ? batchSize : DefaultBatchSize);

        foreach (var batch in batches)
        {
            var batchList = batch.ToList();
            var sql = GenerateBulkUpdateSql(provider, escapedTable, escapedKey, keyColumn, columnNames, batchList.Count);

            // 准备参数：参数名使用未转义的原始列名
            var parameters = new DynamicParameters();
            for (int i = 0; i < batchList.Count; i++)
            {
                var entity = batchList[i];
                var keyValue = keyPropertyInfo.GetValue(entity);
                parameters.Add($"{keyColumn}_{i}", keyValue);

                foreach (var mapping in mappings)
                {
                    if (propertyInfos.TryGetValue(mapping.PropertyName, out var prop))
                    {
                        var value = prop.GetValue(entity);
                        parameters.Add($"{mapping.ColumnName}_{i}", value);
                    }
                }
            }

            totalUpdated += await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        }

        return totalUpdated;
    }

    /// <summary>
    /// 生成批量更新 SQL（根据数据库类型）
    /// </summary>
    /// <param name="keyColumn">未转义的原始主键列名（用于参数名）</param>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    private static string GenerateBulkUpdateSql(
        IDatabaseProvider provider,
        string escapedTable,
        string escapedKey,
        string keyColumn,
        List<string> columnNames,
        int entityCount)
    {
        var databaseType = provider.DatabaseType;

        // SQL Server 和 PostgreSQL 支持 UPDATE ... FROM (VALUES ...) 语法
        if (databaseType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            || databaseType.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return GenerateBulkUpdateSqlUsingFrom(provider, escapedTable, escapedKey, keyColumn, columnNames, entityCount);
        }

        // MySQL 使用 INSERT ... ON DUPLICATE KEY UPDATE
        if (databaseType.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
        {
            return GenerateBulkUpdateSqlUsingInsertOnDuplicate(provider, escapedTable, escapedKey, keyColumn, columnNames, entityCount);
        }

        // 其他数据库回退到 UPDATE ... FROM
        return GenerateBulkUpdateSqlUsingFrom(provider, escapedTable, escapedKey, keyColumn, columnNames, entityCount);
    }

    /// <summary>
    /// 生成使用 UPDATE ... FROM (VALUES ...) 的批量更新 SQL（SQL Server / PostgreSQL）
    /// </summary>
    /// <param name="keyColumn">未转义的原始主键列名（用于参数名）</param>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    private static string GenerateBulkUpdateSqlUsingFrom(
        IDatabaseProvider provider,
        string escapedTable,
        string escapedKey,
        string keyColumn,
        List<string> columnNames,
        int entityCount)
    {
        var escapedColumns = columnNames.Select(p => provider.EscapeIdentifier(p)).ToList();
        var keyAlias = "key_val";
        var escapedKeyAlias = provider.EscapeIdentifier(keyAlias);
        var escapedColumnAliases = columnNames.Select((_, i) => provider.EscapeIdentifier($"val{i}")).ToList();

        // 构建 VALUES 子句：参数名使用未转义的原始列名
        var valuesParts = new List<string>();
        for (int i = 0; i < entityCount; i++)
        {
            var valueParams = new List<string> { $"@{keyColumn}_{i}" };
            valueParams.AddRange(columnNames.Select(col => $"@{col}_{i}"));
            valuesParts.Add($"({string.Join(", ", valueParams)})");
        }

        // 构建 SET 子句。
        // 只有 SQL Server 允许在 SET 左侧写表限定列名（SET tbl.col = ...）；
        // PostgreSQL / SQLite 的 UPDATE ... FROM 语法里 SET 左侧必须是裸列名，
        // 加表限定会直接报语法错误（本方法也是"其它数据库"的回退路径，故按 provider 判定）。
        var qualifySetColumns = provider.DatabaseType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
        var setClauses = escapedColumns.Select((col, i) => qualifySetColumns
            ? $"{escapedTable}.{col} = v.{escapedColumnAliases[i]}"
            : $"{col} = v.{escapedColumnAliases[i]}").ToList();

        var sql = $@"
UPDATE {escapedTable}
SET {string.Join(", ", setClauses)}
FROM (VALUES {string.Join(", ", valuesParts)}) AS v({escapedKeyAlias}, {string.Join(", ", escapedColumnAliases)})
WHERE {escapedTable}.{escapedKey} = v.{escapedKeyAlias}";

        return sql.Trim();
    }

    /// <summary>
    /// 生成使用 INSERT ... ON DUPLICATE KEY UPDATE 的批量更新 SQL（MySQL）
    /// </summary>
    /// <param name="keyColumn">未转义的原始主键列名（用于参数名）</param>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    private static string GenerateBulkUpdateSqlUsingInsertOnDuplicate(
        IDatabaseProvider provider,
        string escapedTable,
        string escapedKey,
        string keyColumn,
        List<string> columnNames,
        int entityCount)
    {
        var allEscapedColumns = new List<string> { escapedKey };
        allEscapedColumns.AddRange(columnNames.Select(p => provider.EscapeIdentifier(p)));

        // 构建 VALUES 子句：参数名使用未转义的原始列名
        var valuesParts = new List<string>();
        for (int i = 0; i < entityCount; i++)
        {
            var valueParams = new List<string> { $"@{keyColumn}_{i}" };
            valueParams.AddRange(columnNames.Select(col => $"@{col}_{i}"));
            valuesParts.Add($"({string.Join(", ", valueParams)})");
        }

        // 构建 ON DUPLICATE KEY UPDATE 子句
        var updateClauses = columnNames.Select(p =>
            $"{provider.EscapeIdentifier(p)} = VALUES({provider.EscapeIdentifier(p)})").ToList();

        var sql = $@"
INSERT INTO {escapedTable} ({string.Join(", ", allEscapedColumns)})
VALUES {string.Join(", ", valuesParts)}
ON DUPLICATE KEY UPDATE {string.Join(", ", updateClauses)}";

        return sql.Trim();
    }

    /// <summary>
    /// 批量删除
    /// </summary>
    /// <param name="batchSize">每批处理的键数量，默认 1000</param>
    internal static async Task<int> BulkDeleteAsync(
        IDbConnection connection,
        IDatabaseProvider provider,
        DbContext dbContext,
        IEnumerable<object> keys,
        Type entityType,
        string? tableName = null,
        string? keyColumn = null,
        IDbTransaction? transaction = null,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(connection);
        Check.NotNull(provider);
        Check.NotNull(dbContext);
        Check.NotNull(keys);
        Check.NotNull(entityType);

        var keyList = keys.ToList();
        if (keyList.Count == 0)
            return 0;

        tableName ??= DapperEntityHelper.GetTableName(entityType, dbContext);
        keyColumn ??= DapperEntityHelper.GetKeyColumnName(entityType, dbContext);

        SqlIdentifierHelper.ThrowIfInvalidIdentifier(tableName, nameof(tableName));
        SqlIdentifierHelper.ThrowIfInvalidIdentifier(keyColumn, nameof(keyColumn));

        var escapedTable = provider.EscapeIdentifier(tableName);
        var escapedKey = provider.EscapeIdentifier(keyColumn);

        var totalDeleted = 0;
        var batches = keyList.Chunk(batchSize > 0 ? batchSize : DefaultBatchSize);

        foreach (var batch in batches)
        {
            var sql = $"DELETE FROM {escapedTable} WHERE {escapedKey} IN @Ids";

            totalDeleted += await connection.ExecuteAsync(
                new CommandDefinition(sql, new { Ids = batch.ToList() }, transaction, cancellationToken: cancellationToken));
        }

        return totalDeleted;
    }

    /// <summary>
    /// 生成批量插入 SQL
    /// </summary>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    private static string GenerateBulkInsertSql(IDatabaseProvider provider, string escapedTable, List<string> columnNames, int entityCount)
    {
        var escapedColumns = columnNames.Select(p => provider.EscapeIdentifier(p)).ToList();
        var columns = string.Join(", ", escapedColumns);

        // 生成 VALUES 子句：参数名使用未转义的原始列名
        var valuesList = new List<string>();
        for (int i = 0; i < entityCount; i++)
        {
            var values = columnNames.Select(col => $"@{col}_{i}").ToList();
            valuesList.Add($"({string.Join(", ", values)})");
        }

        var valuesClause = string.Join(", ", valuesList);
        return $"INSERT INTO {escapedTable} ({columns}) VALUES {valuesClause}";
    }

    /// <summary>
    /// 预缓存实体的 PropertyInfo，使用 CLR 属性名作为 key
    /// </summary>
    private static Dictionary<string, PropertyInfo> CachePropertyInfos<T>(List<ColumnMapping> mappings)
    {
        var result = new Dictionary<string, PropertyInfo>(mappings.Count);
        foreach (var mapping in mappings)
        {
            var prop = typeof(T).GetProperty(mapping.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanRead)
            {
                result[mapping.PropertyName] = prop;
            }
        }
        return result;
    }
}
