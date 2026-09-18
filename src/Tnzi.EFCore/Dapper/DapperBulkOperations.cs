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
    /// <remarks>
    /// <para>
    /// 主键默认<b>写入</b>：框架实体的 Id 一律在应用侧生成（Sequential GUID / Snowflake），库里没有默认值，
    /// 不带主键的 INSERT 对 Guid 键是整批 NOT NULL 违例、对 long 键是库里另发一套 Id 而调用方手里的实体
    /// 与之对不上。键仍是默认值的实体在插入前按 SaveChanges 同一套规则生成 Id；框架不生成的键类型
    /// （如 int）为默认值时抛出而不是交给库分配。真要用数据库自增键，显式传 <paramref name="includeKey"/> = false。
    /// </para>
    /// <para>
    /// 审计列与租户列按 <c>SaveChanges</c> 新增分支同一套规则填充：<c>CreationTime</c> 仍为默认值、
    /// <c>CreatorId</c> / <c>TenantId</c> 仍为 null 时从上下文的当前用户 / 当前租户取值，
    /// <c>ConcurrencyStamp</c> 总是换新；调用方已经赋的值保留。本路径绕过变更跟踪器，
    /// 文件引用追踪、领域事件、软删转换都<b>不会</b>发生。
    /// </para>
    /// <para>
    /// 批大小按「参数总数」封顶（<see cref="IDatabaseProvider.MaxParametersPerCommand"/>），
    /// 不是按行数：SQL Server 每命令 2100 个参数，每行 N 列 1000 行一批在 N ≥ 3 时就越界。
    /// </para>
    /// </remarks>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="connection">数据库连接</param>
    /// <param name="provider">数据库提供者（负责标识符转义与方言差异）</param>
    /// <param name="dbContext">用于解析实体到表/列映射的上下文（审计协作者也取自它）</param>
    /// <param name="entities">待插入实体</param>
    /// <param name="tableName">目标表名；为空时从映射解析</param>
    /// <param name="transaction">外部事务；为空则由连接自行处理</param>
    /// <param name="batchSize">每批处理的实体数量上限，默认 1000；实际每批行数还会按参数总数封顶</param>
    /// <param name="includeKey">是否写入主键列；默认 true。仅数据库自增键的表才传 false</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
    public static async Task<int> BulkInsertAsync<T>(
        IDbConnection connection,
        IDatabaseProvider provider,
        DbContext dbContext,
        IEnumerable<T> entities,
        string? tableName = null,
        IDbTransaction? transaction = null,
        int batchSize = DefaultBatchSize,
        bool includeKey = true,
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

        var mappings = DapperEntityHelper.GetColumnMappings<T>(dbContext, excludeKey: !includeKey);
        if (mappings.Count == 0)
            throw new InvalidOperationException($"No properties found for type {typeof(T).Name}");

        // 验证所有列名
        foreach (var mapping in mappings)
        {
            SqlIdentifierHelper.ThrowIfInvalidIdentifier(mapping.ColumnName, "column");
        }

        if (includeKey)
        {
            EnsureKeysAssigned(dbContext, entityList);
        }

        // 创建审计与租户列按 SaveChanges 新增分支同一套规则填充（协作者取自上下文自身）。
        // 这条路径绕过变更跟踪器，不填就是一批 CreationTime 0001-01-01、TenantId 为 null 的行安静落库。
        AuditPropertyHelper.ApplyCreationAudit(dbContext, entityList);

        // 预缓存 PropertyInfo
        var propertyInfos = CachePropertyInfos<T>(mappings);

        var totalInserted = 0;
        var rowsPerBatch = CalculateRowsPerBatch(batchSize, mappings.Count, provider.MaxParametersPerCommand);
        var batches = entityList.Chunk(rowsPerBatch);

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
    /// <remarks>
    /// 修改审计（<c>LastModificationTime</c> / <c>LastModifierId</c> / 换 <c>ConcurrencyStamp</c>）按
    /// <c>SaveChanges</c> 修改分支同一套规则填充；其余非主键列从实体逐字写入，
    /// 所以创建审计列要带着原值传进来（从库里读出来再改，不要 <c>new</c> 一个只填业务字段的实体）。
    /// </remarks>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="connection">数据库连接</param>
    /// <param name="provider">数据库提供者（负责标识符转义与方言差异）</param>
    /// <param name="dbContext">用于解析实体到表/列映射的上下文</param>
    /// <param name="entities">待更新实体</param>
    /// <param name="tableName">目标表名；为空时从映射解析</param>
    /// <param name="keyColumn">主键列名；为空时从映射解析</param>
    /// <param name="transaction">外部事务；为空则由连接自行处理</param>
    /// <param name="batchSize">每批处理的实体数量，默认 1000</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
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

        // 修改审计按 SaveChanges 修改分支同一套规则填充（修改人 / 修改时间 / 换并发戳）。
        AuditPropertyHelper.ApplyModificationAudit(dbContext, entityList);

        // 预缓存 PropertyInfo
        var propertyInfos = CachePropertyInfos<T>(mappings);

        var columnNames = mappings.Select(m => m.ColumnName).ToList();
        var totalUpdated = 0;
        var rowsPerBatch = CalculateRowsPerBatch(batchSize, mappings.Count + 1, provider.MaxParametersPerCommand);
        var batches = entityList.Chunk(rowsPerBatch);

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
    /// <param name="provider">数据库提供者（决定生成哪种方言的 SQL）</param>
    /// <param name="escapedTable">已转义的表名</param>
    /// <param name="escapedKey">已转义的主键列名</param>
    /// <param name="keyColumn">未转义的原始主键列名（用于参数名）</param>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    /// <param name="entityCount">本批实体数量（决定 VALUES 子句的组数）</param>
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

        // 其他数据库回退到 UPDATE ... FROM，行值表放进 CTE：
        // 派生表带列别名（AS v(a, b)）是 SQL Server / PostgreSQL 的写法，SQLite 不认，
        // 而 WITH v(a, b) AS (VALUES ...) 三家都认。
        return GenerateBulkUpdateSqlUsingFrom(provider, escapedTable, escapedKey, keyColumn, columnNames, entityCount, valuesAsCte: true);
    }

    /// <summary>
    /// 生成使用 UPDATE ... FROM (VALUES ...) 的批量更新 SQL（SQL Server / PostgreSQL）
    /// </summary>
    /// <param name="provider">数据库提供者（负责标识符转义）</param>
    /// <param name="escapedTable">已转义的表名</param>
    /// <param name="escapedKey">已转义的主键列名</param>
    /// <param name="keyColumn">未转义的原始主键列名（用于参数名）</param>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    /// <param name="entityCount">本批实体数量（决定 VALUES 子句的组数）</param>
    /// <param name="valuesAsCte">行值表写成 <c>WITH v(...) AS (VALUES ...)</c> 而不是带列别名的派生表（回退方言）</param>
    private static string GenerateBulkUpdateSqlUsingFrom(
        IDatabaseProvider provider,
        string escapedTable,
        string escapedKey,
        string keyColumn,
        List<string> columnNames,
        int entityCount,
        bool valuesAsCte = false)
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

        var aliasList = $"{escapedKeyAlias}, {string.Join(", ", escapedColumnAliases)}";
        var valuesClause = $"VALUES {string.Join(", ", valuesParts)}";

        var sql = valuesAsCte
            ? $@"
WITH v({aliasList}) AS ({valuesClause})
UPDATE {escapedTable}
SET {string.Join(", ", setClauses)}
FROM v
WHERE {escapedTable}.{escapedKey} = v.{escapedKeyAlias}"
            : $@"
UPDATE {escapedTable}
SET {string.Join(", ", setClauses)}
FROM ({valuesClause}) AS v({aliasList})
WHERE {escapedTable}.{escapedKey} = v.{escapedKeyAlias}";

        return sql.Trim();
    }

    /// <summary>
    /// 生成使用 INSERT ... ON DUPLICATE KEY UPDATE 的批量更新 SQL（MySQL）
    /// </summary>
    /// <param name="provider">数据库提供者（负责标识符转义）</param>
    /// <param name="escapedTable">已转义的表名</param>
    /// <param name="escapedKey">已转义的主键列名</param>
    /// <param name="keyColumn">未转义的原始主键列名（用于参数名）</param>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    /// <param name="entityCount">本批实体数量（决定 VALUES 子句的组数）</param>
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
    /// <param name="connection">数据库连接</param>
    /// <param name="provider">数据库提供者（负责标识符转义与方言差异）</param>
    /// <param name="dbContext">用于解析实体到表/列映射的上下文</param>
    /// <param name="keys">待删除记录的主键值</param>
    /// <param name="entityType">实体类型（用于解析表名与主键列）</param>
    /// <param name="tableName">目标表名；为空时从映射解析</param>
    /// <param name="keyColumn">主键列名；为空时从映射解析</param>
    /// <param name="transaction">外部事务；为空则由连接自行处理</param>
    /// <param name="batchSize">每批处理的键数量，默认 1000</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
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
        var keysPerBatch = CalculateRowsPerBatch(batchSize, 1, provider.MaxParametersPerCommand);
        var batches = keyList.Chunk(keysPerBatch);

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
    /// <param name="provider">数据库提供者（负责标识符转义）</param>
    /// <param name="escapedTable">已转义的表名</param>
    /// <param name="columnNames">未转义的原始列名列表（用于参数名）</param>
    /// <param name="entityCount">本批实体数量（决定 VALUES 子句的组数）</param>
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
    /// 计算每批的行数：调用方请求的批大小与「参数上限 ÷ 每行参数数」取小。
    /// </summary>
    /// <param name="requestedBatchSize">调用方请求的每批行数；非正数取默认 1000</param>
    /// <param name="parametersPerRow">每行占用的参数个数</param>
    /// <param name="maxParametersPerCommand">数据库单条命令的参数上限（见 <see cref="IDatabaseProvider.MaxParametersPerCommand"/>）</param>
    /// <exception cref="InvalidOperationException">单行参数数已超过上限，任何分批都装不下</exception>
    public static int CalculateRowsPerBatch(int requestedBatchSize, int parametersPerRow, int maxParametersPerCommand)
    {
        var requested = requestedBatchSize > 0 ? requestedBatchSize : DefaultBatchSize;
        if (parametersPerRow <= 0 || maxParametersPerCommand <= 0)
        {
            return requested;
        }

        var rowsWithinLimit = maxParametersPerCommand / parametersPerRow;
        if (rowsWithinLimit < 1)
        {
            throw new InvalidOperationException(
                $"A single row needs {parametersPerRow} parameters, which exceeds the {maxParametersPerCommand} parameters the database accepts per command.");
        }

        return Math.Min(requested, rowsWithinLimit);
    }

    /// <summary>
    /// 主键仍为默认值的实体在插入前按 SaveChanges 同一套规则生成 Id；生成不了的类型抛出。
    /// 复合主键不生成，任一键属性为默认值即拒绝。
    /// </summary>
    private static void EnsureKeysAssigned<T>(DbContext dbContext, List<T> entities) where T : class
    {
        var entityType = dbContext.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} is not registered in DbContext {dbContext.GetType().Name}");
        var keyProperties = entityType.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a primary key");

        var keyAccessors = keyProperties
            .Select(p => typeof(T).GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Cannot find key property '{p.Name}' on type {typeof(T).Name}"))
            .ToList();
        var canGenerate = keyAccessors.Count == 1 && keyAccessors[0].CanWrite;

        for (var i = 0; i < entities.Count; i++)
        {
            foreach (var key in keyAccessors)
            {
                if (!IdGenerationHelper.IsDefaultValue(key.GetValue(entities[i]), key.PropertyType))
                {
                    continue;
                }

                var generated = canGenerate ? IdGenerationHelper.GenerateId(dbContext, typeof(T), key.PropertyType) : null;
                if (generated == null)
                {
                    throw new InvalidOperationException(
                        $"Entity {typeof(T).Name} at index {i} has a default value for key '{key.Name}' ({key.PropertyType.Name}) and the framework cannot generate one. " +
                        "Assign the key before calling BulkInsertAsync, or pass includeKey: false if the database generates it.");
                }

                key.SetValue(entities[i], generated);
            }
        }
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
