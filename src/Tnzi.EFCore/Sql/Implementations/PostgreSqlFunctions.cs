namespace Tnzi.EFCore.Sql.Implementations;

/// <summary>
/// PostgreSQL 数据库的 SQL 函数实现
/// </summary>
internal sealed class PostgreSqlFunctions : ISqlFunctions
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static readonly PostgreSqlFunctions Instance = new();

    private PostgreSqlFunctions() { }

    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.PostgreSQL;

    #region 日期时间函数

    /// <inheritdoc />
    /// <remarks>
    /// ★ 面向 <c>timestamp with time zone</c> 列（Npgsql 对 <see cref="DateTime"/> 的默认映射）：<c>NOW()</c> 是绝对时刻，
    /// 与会话时区无关。<c>(NOW() AT TIME ZONE 'UTC')</c> 得到的是不带时区的 UTC 墙钟，赋给 timestamptz 列时
    /// PostgreSQL 按会话 <c>TimeZone</c> 把它当本地时间再换算一次，非 UTC 时区的服务器上默认值整体偏移一个时区差。
    /// </remarks>
    public string CurrentUtcTime => "NOW()";

    /// <inheritdoc />
    public string CurrentLocalTime => "NOW()";

    /// <inheritdoc />
    /// <remarks>
    /// 先把当前时刻换到 UTC 再取日期。<c>CURRENT_DATE</c> 是会话时区的本地日期，从它出发在每天的一段时间里会差一天。
    /// </remarks>
    public string CurrentUtcDate => "(NOW() AT TIME ZONE 'UTC')::DATE";

    /// <inheritdoc />
    public string CurrentLocalDate => "CURRENT_DATE";

    /// <inheritdoc />
    public string CurrentTimestamp => "CURRENT_TIMESTAMP";

    #endregion

    #region GUID/UUID 函数

    /// <inheritdoc />
    public string NewGuid => "gen_random_uuid()";

    /// <inheritdoc />
    /// <remarks>
    /// 注意：需要先执行 CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
    /// 如果未安装扩展，可以回退使用 gen_random_uuid()
    /// </remarks>
    public string NewSequentialGuid => "uuid_generate_v1()";

    #endregion

    #region 布尔值常量

    /// <inheritdoc />
    public string True => "TRUE";

    /// <inheritdoc />
    public string False => "FALSE";

    #endregion

    #region 数学函数

    /// <inheritdoc />
    /// <remarks>
    /// PostgreSQL 的 RANDOM() 返回 0 到 1 之间的值，与其他数据库一致
    /// </remarks>
    public string Random => "RANDOM()";

    #endregion
}
