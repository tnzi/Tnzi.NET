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
    public string CurrentUtcTime => "(NOW() AT TIME ZONE 'UTC')";

    /// <inheritdoc />
    public string CurrentLocalTime => "NOW()";

    /// <inheritdoc />
    public string CurrentUtcDate => "(CURRENT_DATE AT TIME ZONE 'UTC')::DATE";

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
