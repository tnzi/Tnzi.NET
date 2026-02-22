
namespace Tnzi.EFCore.Exceptions;

/// <summary>
/// 数据库异常基类
/// </summary>
public class DatabaseException : InfrastructureException
{
    /// <summary>
    /// 连接字符串（不包含敏感信息）
    /// </summary>
    public string? ConnectionString { get; }
    
    /// <summary>
    /// 初始化一个<see cref="DatabaseException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="isRetryable">是否可重试</param>
    /// <param name="innerException">内部异常</param>
    public DatabaseException(
        string message,
        string? connectionString = null,
        bool isRetryable = false,
        Exception? innerException = null)
        : base("Database", message, isRetryable, innerException)
    {
        ConnectionString = SanitizeConnectionString(connectionString);
        if (ConnectionString != null)
        {
            this.WithData("ConnectionString", ConnectionString);
        }
    }

    /// <summary>
    /// 脱敏连接字符串中的敏感信息（密码等）
    /// </summary>
    private static string? SanitizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        // 使用正则替换 Password/Pwd/Pass 的值为 ***
        return Regex.Replace(connectionString,
            @"(Password|Pwd|Pass)\s*=\s*[^;]*",
            "$1=***",
            RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// 数据库连接异常
/// </summary>
public class DbConnectionException : DatabaseException
{
    /// <summary>
    /// 初始化一个<see cref="DbConnectionException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="innerException">内部异常</param>
    public DbConnectionException(string message, string? connectionString = null, Exception? innerException = null)
        : base(message, connectionString, true, innerException)
    {
    }
}

/// <summary>
/// 数据库迁移异常
/// </summary>
public class DbMigrationException : DatabaseException
{
    /// <summary>
    /// 迁移名称
    /// </summary>
    public string? MigrationName { get; }
    
    /// <summary>
    /// 初始化一个<see cref="DbMigrationException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="migrationName">迁移名称</param>
    /// <param name="innerException">内部异常</param>
    public DbMigrationException(string message, string? migrationName = null, Exception? innerException = null)
        : base(message, null, false, innerException)
    {
        MigrationName = migrationName;
        if (migrationName != null)
        {
            this.WithData("MigrationName", migrationName);
        }
    }
}

/// <summary>
/// 数据库并发异常
/// </summary>
public class DbConcurrencyException : DatabaseException
{
    /// <summary>
    /// 初始化一个<see cref="DbConcurrencyException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    public DbConcurrencyException(string message, Exception? innerException = null)
        : base(message, null, true, innerException)
    {
    }
}

/// <summary>
/// 数据库查询异常
/// </summary>
public class DbQueryException : DatabaseException
{
    /// <summary>
    /// SQL查询语句
    /// </summary>
    public string? Query { get; }
    
    /// <summary>
    /// 初始化一个<see cref="DbQueryException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="query">SQL查询语句</param>
    /// <param name="innerException">内部异常</param>
    public DbQueryException(string message, string? query = null, Exception? innerException = null)
        : base(message, null, false, innerException)
    {
        Query = query;
        if (query != null)
        {
            this.WithData("Query", query);
        }
    }
}
