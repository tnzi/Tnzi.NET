
namespace Tnzi.EFCore.Dapper.Internal;

/// <summary>
/// 列名与属性名的映射关系
/// </summary>
/// <param name="ColumnName">数据库列名（用于 SQL 语句）</param>
/// <param name="PropertyName">CLR 属性名（用于反射获取值）</param>
public record struct ColumnMapping(string ColumnName, string PropertyName);

/// <summary>
/// Dapper 实体辅助类
/// 用于从 EF Core Model 获取表名和列名信息
/// </summary>
public static class DapperEntityHelper
{
    // 缓存表名：Key = (EntityType.FullName, DbContextType.FullName) -> TableName
    private static readonly ConcurrentDictionary<(string EntityType, string DbContextType), string> _tableNameCache = new();

    // 缓存 Schema：Key = (EntityType.FullName, DbContextType.FullName) -> Schema (nullable)
    private static readonly ConcurrentDictionary<(string EntityType, string DbContextType), string?> _schemaCache = new();

    // 缓存主键信息：Key = (EntityType.FullName, DbContextType.FullName) -> (ColumnName, PropertyName)
    private static readonly ConcurrentDictionary<(string EntityType, string DbContextType), ColumnMapping> _keyColumnCache = new();

    // 缓存列映射列表：Key = (EntityType.FullName, DbContextType.FullName, ExcludeKey) -> ColumnMappings
    private static readonly ConcurrentDictionary<(string EntityType, string DbContextType, bool ExcludeKey), List<ColumnMapping>> _columnMappingsCache = new();

    /// <summary>
    /// 从 DbContext 获取实体的表名
    /// </summary>
    public static string GetTableName<TEntity>(DbContext dbContext)
    {
        return GetTableName(typeof(TEntity), dbContext);
    }

    /// <summary>
    /// 从 DbContext 获取实体的表名（带缓存）
    /// </summary>
    public static string GetTableName(Type entityType, DbContext dbContext)
    {
        var cacheKey = (entityType.FullName ?? entityType.Name, dbContext.GetType().FullName ?? dbContext.GetType().Name);

        return _tableNameCache.GetOrAdd(cacheKey, _ =>
        {
            var entityTypeMetadata = dbContext.Model.FindEntityType(entityType);
            if (entityTypeMetadata == null)
            {
                throw new InvalidOperationException($"Entity type {entityType.Name} is not registered in DbContext {dbContext.GetType().Name}");
            }

            var tableName = entityTypeMetadata.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                // 如果没有显式配置表名，使用实体类型名称
                tableName = entityType.Name;
            }

            return tableName;
        });
    }

    /// <summary>
    /// 从 DbContext 获取实体的 Schema（带缓存）
    /// </summary>
    public static string? GetSchema(Type entityType, DbContext dbContext)
    {
        var cacheKey = (entityType.FullName ?? entityType.Name, dbContext.GetType().FullName ?? dbContext.GetType().Name);

        return _schemaCache.GetOrAdd(cacheKey, _ =>
        {
            var entityTypeMetadata = dbContext.Model.FindEntityType(entityType);
            return entityTypeMetadata?.GetSchema();
        });
    }

    /// <summary>
    /// 从 DbContext 获取实体的 Schema
    /// </summary>
    public static string? GetSchema<TEntity>(DbContext dbContext)
    {
        return GetSchema(typeof(TEntity), dbContext);
    }

    /// <summary>
    /// 从 DbContext 获取实体的主键列名
    /// </summary>
    public static string GetKeyColumnName<TEntity>(DbContext dbContext)
    {
        return GetKeyColumnName(typeof(TEntity), dbContext);
    }

    /// <summary>
    /// 从 DbContext 获取实体的主键列名（带缓存）
    /// </summary>
    public static string GetKeyColumnName(Type entityType, DbContext dbContext)
    {
        return GetKeyMapping(entityType, dbContext).ColumnName;
    }

    /// <summary>
    /// 获取主键的列名与属性名映射（带缓存）
    /// </summary>
    public static ColumnMapping GetKeyMapping(Type entityType, DbContext dbContext)
    {
        var cacheKey = (entityType.FullName ?? entityType.Name, dbContext.GetType().FullName ?? dbContext.GetType().Name);

        return _keyColumnCache.GetOrAdd(cacheKey, _ =>
        {
            var entityTypeMetadata = dbContext.Model.FindEntityType(entityType);
            if (entityTypeMetadata == null)
            {
                throw new InvalidOperationException($"Entity type {entityType.Name} is not registered in DbContext {dbContext.GetType().Name}");
            }

            var primaryKey = entityTypeMetadata.FindPrimaryKey();
            if (primaryKey == null || primaryKey.Properties.Count == 0)
            {
                throw new InvalidOperationException($"Entity type {entityType.Name} does not have a primary key");
            }

            // 获取第一个主键属性（支持复合主键，但通常使用第一个）
            var keyProperty = primaryKey.Properties[0];
            var columnName = keyProperty.GetColumnName() ?? keyProperty.Name;
            var propertyName = keyProperty.Name;

            return new ColumnMapping(columnName, propertyName);
        });
    }

    /// <summary>
    /// 获取实体的列名与属性名映射列表（带缓存）
    /// </summary>
    public static List<ColumnMapping> GetColumnMappings<TEntity>(DbContext dbContext, bool excludeKey = true)
    {
        return GetColumnMappings(typeof(TEntity), dbContext, excludeKey);
    }

    /// <summary>
    /// 获取实体的列名与属性名映射列表（带缓存）
    /// </summary>
    public static List<ColumnMapping> GetColumnMappings(Type entityType, DbContext dbContext, bool excludeKey = true)
    {
        var cacheKey = (entityType.FullName ?? entityType.Name, dbContext.GetType().FullName ?? dbContext.GetType().Name, excludeKey);

        return _columnMappingsCache.GetOrAdd(cacheKey, _ =>
        {
            var entityTypeMetadata = dbContext.Model.FindEntityType(entityType);
            if (entityTypeMetadata == null)
            {
                throw new InvalidOperationException($"Entity type {entityType.Name} is not registered in DbContext {dbContext.GetType().Name}");
            }

            var properties = entityTypeMetadata.GetProperties();

            if (excludeKey)
            {
                var primaryKey = entityTypeMetadata.FindPrimaryKey();
                var keyPropertyNames = primaryKey?.Properties.Select(p => p.Name).ToHashSet() ?? [];
                return properties
                    .Where(p => !keyPropertyNames.Contains(p.Name))
                    .Select(p => new ColumnMapping(p.GetColumnName() ?? p.Name, p.Name))
                    .ToList();
            }

            return properties
                .Select(p => new ColumnMapping(p.GetColumnName() ?? p.Name, p.Name))
                .ToList();
        });
    }

    /// <summary>
    /// 获取实体的所有列名（排除主键）
    /// 保留向后兼容性
    /// </summary>
    public static List<string> GetPropertyNames<TEntity>(DbContext dbContext, bool excludeKey = true)
    {
        return GetColumnMappings<TEntity>(dbContext, excludeKey)
            .Select(m => m.ColumnName)
            .ToList();
    }

    /// <summary>
    /// 获取实体的所有列名（排除主键，带缓存）
    /// 保留向后兼容性
    /// </summary>
    public static List<string> GetPropertyNames(Type entityType, DbContext dbContext, bool excludeKey = true)
    {
        return GetColumnMappings(entityType, dbContext, excludeKey)
            .Select(m => m.ColumnName)
            .ToList();
    }
}
