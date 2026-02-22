
namespace Tnzi.EFCore.Options;

/// <summary>
/// 连接字符串构建器
/// 用于将连接池配置应用到连接字符串
/// </summary>
public static class ConnectionStringBuilder
{
    /// <summary>
    /// 将连接池配置应用到连接字符串
    /// 已存在于连接字符串中的参数不会被覆盖
    /// </summary>
    /// <param name="baseConnectionString">基础连接字符串</param>
    /// <param name="poolOptions">连接池配置</param>
    /// <param name="provider">数据库提供者</param>
    /// <returns>合并后的连接字符串</returns>
    public static string ApplyPoolingOptions(
        string baseConnectionString,
        ConnectionPoolOptions? poolOptions,
        DatabaseProvider provider)
    {
        if (string.IsNullOrEmpty(baseConnectionString) || poolOptions == null || !poolOptions.HasConfiguration)
        {
            return baseConnectionString;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = baseConnectionString };

        // 根据数据库类型使用不同的参数名
        switch (provider)
        {
            case DatabaseProvider.SqlServer:
                ApplySqlServerOptions(builder, poolOptions);
                break;
            case DatabaseProvider.PostgreSQL:
                ApplyPostgreSqlOptions(builder, poolOptions);
                break;
            case DatabaseProvider.MySql:
                ApplyMySqlOptions(builder, poolOptions);
                break;
            case DatabaseProvider.Sqlite:
                ApplySqliteOptions(builder, poolOptions);
                break;
            default:
                // 使用通用参数名
                ApplyGenericOptions(builder, poolOptions);
                break;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// 应用 SQL Server 连接池配置
    /// </summary>
    private static void ApplySqlServerOptions(DbConnectionStringBuilder builder, ConnectionPoolOptions options)
    {
        // SQL Server 连接池参数
        // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlconnection.connectionstring

        SetIfNotExists(builder, "Max Pool Size", options.MaxPoolSize);
        SetIfNotExists(builder, "Min Pool Size", options.MinPoolSize);
        SetIfNotExists(builder, "Connection Timeout", options.ConnectionTimeout);
        SetIfNotExists(builder, "Command Timeout", options.CommandTimeout);
        SetIfNotExists(builder, "Pooling", options.Pooling);
    }

    /// <summary>
    /// 应用 PostgreSQL 连接池配置
    /// </summary>
    private static void ApplyPostgreSqlOptions(DbConnectionStringBuilder builder, ConnectionPoolOptions options)
    {
        // Npgsql 连接池参数
        // https://www.npgsql.org/doc/connection-string-parameters.html

        SetIfNotExists(builder, "Maximum Pool Size", options.MaxPoolSize);
        SetIfNotExists(builder, "Minimum Pool Size", options.MinPoolSize);
        SetIfNotExists(builder, "Timeout", options.ConnectionTimeout);
        SetIfNotExists(builder, "Command Timeout", options.CommandTimeout);
        SetIfNotExists(builder, "Connection Idle Lifetime", options.ConnectionIdleTimeout);
        SetIfNotExists(builder, "Pooling", options.Pooling);
    }

    /// <summary>
    /// 应用 MySQL 连接池配置
    /// </summary>
    private static void ApplyMySqlOptions(DbConnectionStringBuilder builder, ConnectionPoolOptions options)
    {
        // MySql.Data / MySqlConnector 连接池参数
        // https://mysqlconnector.net/connection-options/

        SetIfNotExists(builder, "Max Pool Size", options.MaxPoolSize);
        SetIfNotExists(builder, "Min Pool Size", options.MinPoolSize);
        SetIfNotExists(builder, "Connection Timeout", options.ConnectionTimeout);
        SetIfNotExists(builder, "Default Command Timeout", options.CommandTimeout);
        SetIfNotExists(builder, "Connection Idle Timeout", options.ConnectionIdleTimeout);
        SetIfNotExists(builder, "Pooling", options.Pooling);
    }

    /// <summary>
    /// 应用 SQLite 连接池配置
    /// </summary>
    private static void ApplySqliteOptions(DbConnectionStringBuilder builder, ConnectionPoolOptions options)
    {
        // SQLite 连接池参数
        // https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings

        // SQLite 的连接池配置较为有限
        SetIfNotExists(builder, "Pooling", options.Pooling);
        // SQLite 使用 Default Timeout 作为命令超时
        SetIfNotExists(builder, "Default Timeout", options.CommandTimeout);
    }

    /// <summary>
    /// 应用通用连接池配置（用于未知数据库类型）
    /// </summary>
    private static void ApplyGenericOptions(DbConnectionStringBuilder builder, ConnectionPoolOptions options)
    {
        SetIfNotExists(builder, "Max Pool Size", options.MaxPoolSize);
        SetIfNotExists(builder, "Min Pool Size", options.MinPoolSize);
        SetIfNotExists(builder, "Connection Timeout", options.ConnectionTimeout);
        SetIfNotExists(builder, "Command Timeout", options.CommandTimeout);
        SetIfNotExists(builder, "Pooling", options.Pooling);
    }

    /// <summary>
    /// 如果连接字符串中不存在该参数，则设置
    /// </summary>
    private static void SetIfNotExists(DbConnectionStringBuilder builder, string key, int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        // 检查各种可能的参数名变体
        var variants = GetKeyVariants(key);
        foreach (var variant in variants)
        {
            if (builder.ContainsKey(variant))
            {
                return; // 已存在，不覆盖
            }
        }

        builder[key] = value.Value;
    }

    /// <summary>
    /// 如果连接字符串中不存在该参数，则设置
    /// </summary>
    private static void SetIfNotExists(DbConnectionStringBuilder builder, string key, bool value)
    {
        var variants = GetKeyVariants(key);
        foreach (var variant in variants)
        {
            if (builder.ContainsKey(variant))
            {
                return; // 已存在，不覆盖
            }
        }

        builder[key] = value;
    }

    /// <summary>
    /// 获取参数名的各种变体（用于检查是否已存在）
    /// </summary>
    private static IEnumerable<string> GetKeyVariants(string key)
    {
        yield return key;
        yield return key.Replace(" ", "");
        yield return key.ToLowerInvariant();
        yield return key.Replace(" ", "").ToLowerInvariant();
        yield return key.Replace(" ", "_");
    }
}