
namespace Tnzi.EFCore.Extensions;

/// <summary>
/// DbUpdateException 扩展方法
/// </summary>
public static class DbUpdateExceptionExtensions
{
    // 驱动的唯一约束错误码。按异常类型名 + 属性读取，不引用任何驱动包。
    private const string PostgreSqlUniqueViolation = "23505";
    private static readonly int[] SqlServerUniqueViolations = [2601, 2627];          // 唯一索引 / 唯一约束
    private static readonly int[] MySqlUniqueViolations = [1062, 1586];              // ER_DUP_ENTRY / ER_DUP_ENTRY_WITH_KEY_NAME
    private static readonly int[] SqliteUniqueViolations = [2067, 1555];             // SQLITE_CONSTRAINT_UNIQUE / _PRIMARYKEY

    /// <summary>
    /// 检查异常是否是数据库唯一约束冲突
    /// </summary>
    /// <param name="exception">数据库更新异常</param>
    /// <returns>如果是唯一约束冲突返回 true，否则返回 false</returns>
    /// <remarks>
    /// ★★ 先按驱动的错误码判定（SQL Server 2601/2627、PostgreSQL SQLSTATE 23505、MySQL 1062/1586、
    /// SQLite 扩展码 2067/1555），认得出驱动就以错误码为准；认不出才回退到消息文本。
    /// 此前只看英文消息：MySQL 报的是 <c>Duplicate entry 'x' for key '...'</c>，一个关键词都不中；
    /// SQL Server 与 PostgreSQL 的消息随服务器语言本地化。漏判时调用方把本该 409 的冲突答成 500，
    /// 依赖它重试的并发首插直接失败。
    /// </remarks>
    public static bool IsUniqueConstraintViolation(this DbUpdateException exception)
    {
        var inner = exception?.InnerException;
        if (inner == null)
            return false;

        var classified = ClassifyByErrorCode(inner);
        if (classified.HasValue)
            return classified.Value;

        var message = inner.Message;
        if (string.IsNullOrEmpty(message))
            return false;

        return message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按驱动错误码判定；认不出驱动（或读不到错误码）返回 null。
    /// </summary>
    private static bool? ClassifyByErrorCode(Exception inner)
    {
        // SQLSTATE 23505 只表示唯一约束冲突（PostgreSQL 等按标准细分 SQLSTATE 的驱动）；
        // MySQL 的 SQLSTATE 是笼统的 23000，不能据此判否，交给下面按错误号判
        if (inner is DbException { SqlState: PostgreSqlUniqueViolation })
            return true;

        return inner.GetType().Name switch
        {
            "PostgresException" => false,
            "SqlException" => ReadInt(inner, "Number") is { } number ? SqlServerUniqueViolations.Contains(number) : null,
            "MySqlException" => ReadInt(inner, "Number") is { } number ? MySqlUniqueViolations.Contains(number) : null,
            "SqliteException" => ReadInt(inner, "SqliteExtendedErrorCode") is { } code ? SqliteUniqueViolations.Contains(code) : null,
            _ => null
        };
    }

    private static int? ReadInt(Exception exception, string propertyName)
        => exception.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(exception) as int?;
}
