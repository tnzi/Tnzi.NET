
namespace Tnzi.EFCore.Dapper.Providers;

/// <summary>
/// MySQL 数据库提供者
/// </summary>
public class MySqlProvider : IDatabaseProvider
{
    // 缓存 MySQL 连接类型，避免重复反射
    // volatile 确保双重检查锁模式的线程安全
    private static volatile Type? _cachedConnectionType;
    private static readonly object _lock = new();

    public string DatabaseType => "MySQL";

    public string EscapeIdentifier(string identifier)
    {
        // MySQL 使用反引号
        return $"`{identifier}`";
    }

    public string ApplyPaging(string sql, int offset, int limit)
    {
        // MySQL 使用 LIMIT ... OFFSET ...
        return $"{sql} LIMIT {limit} OFFSET {offset}";
    }

    public string ApplyReturningId(string sql, string keyColumn)
    {
        // MySQL 使用 LAST_INSERT_ID()
        return $"{sql}; SELECT LAST_INSERT_ID()";
    }

    public IDbConnection CreateConnection(string connectionString)
    {
        var connectionType = GetCachedConnectionType();
        
        if (connectionType != null)
        {
            return (IDbConnection)Activator.CreateInstance(connectionType, connectionString)!;
        }
        
        // 如果 MySQL 驱动不可用，抛出异常
        throw new InvalidOperationException("MySql.Data or MySqlConnector package is required for MySQL support. Please install one of these NuGet packages.");
    }

    /// <summary>
    /// 获取缓存的 MySQL 连接类型
    /// </summary>
    private static Type? GetCachedConnectionType()
    {
        if (_cachedConnectionType != null)
            return _cachedConnectionType;

        lock (_lock)
        {
            if (_cachedConnectionType != null)
                return _cachedConnectionType;

            // 优先尝试 MySqlConnector（更现代），然后尝试 MySql.Data
            _cachedConnectionType = Type.GetType("MySqlConnector.MySqlConnection, MySqlConnector")
                                 ?? Type.GetType("MySql.Data.MySqlClient.MySqlConnection, MySql.Data");
        }

        return _cachedConnectionType;
    }
}