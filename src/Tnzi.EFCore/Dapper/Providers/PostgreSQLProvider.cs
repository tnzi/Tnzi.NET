
namespace Tnzi.EFCore.Dapper.Providers;

/// <summary>
/// PostgreSQL 数据库提供者
/// </summary>
public class PostgreSQLProvider : IDatabaseProvider
{
    // 缓存 Npgsql 连接类型，避免重复反射
    // volatile 确保双重检查锁模式的线程安全
    private static volatile Type? _cachedConnectionType;
    private static readonly object _lock = new();

    public string DatabaseType => "PostgreSQL";

    public string EscapeIdentifier(string identifier)
    {
        // PostgreSQL 使用双引号
        return $"\"{identifier}\"";
    }

    public string ApplyPaging(string sql, int offset, int limit)
    {
        // PostgreSQL 使用 LIMIT ... OFFSET ...
        return $"{sql} LIMIT {limit} OFFSET {offset}";
    }

    public string ApplyReturningId(string sql, string keyColumn)
    {
        // 验证标识符，防止 SQL 注入
        Internal.SqlIdentifierHelper.ThrowIfInvalidIdentifier(keyColumn, nameof(keyColumn));
        // PostgreSQL 使用 RETURNING
        return $"{sql} RETURNING {EscapeIdentifier(keyColumn)}";
    }

    public IDbConnection CreateConnection(string connectionString)
    {
        var connectionType = GetCachedConnectionType();
        
        if (connectionType != null)
        {
            return (IDbConnection)Activator.CreateInstance(connectionType, connectionString)!;
        }
        
        // 如果 Npgsql 不可用，抛出异常
        throw new InvalidOperationException("Npgsql package is required for PostgreSQL support. Please install Npgsql NuGet package.");
    }

    /// <summary>
    /// 获取缓存的 Npgsql 连接类型
    /// </summary>
    private static Type? GetCachedConnectionType()
    {
        if (_cachedConnectionType != null)
            return _cachedConnectionType;

        lock (_lock)
        {
            if (_cachedConnectionType != null)
                return _cachedConnectionType;

            _cachedConnectionType = Type.GetType("Npgsql.NpgsqlConnection, Npgsql");
        }

        return _cachedConnectionType;
    }
}