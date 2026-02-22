namespace Tnzi.EFCore.Sql.Implementations;

/// <summary>
/// MySQL 数据库的 SQL 函数实现
/// </summary>
internal sealed class MySqlFunctions : ISqlFunctions
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static readonly MySqlFunctions Instance = new();

    private MySqlFunctions() { }

    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.MySql;

    #region 日期时间函数

    /// <inheritdoc />
    public string CurrentUtcTime => "UTC_TIMESTAMP()";

    /// <inheritdoc />
    public string CurrentLocalTime => "NOW()";

    /// <inheritdoc />
    public string CurrentUtcDate => "UTC_DATE()";

    /// <inheritdoc />
    public string CurrentLocalDate => "CURDATE()";

    /// <inheritdoc />
    /// <remarks>
    /// MySQL 8.0+ 支持 CURRENT_TIMESTAMP(6) 获取微秒精度
    /// </remarks>
    public string CurrentTimestamp => "CURRENT_TIMESTAMP(6)";

    #endregion

    #region GUID/UUID 函数

    /// <inheritdoc />
    public string NewGuid => "UUID()";

    /// <inheritdoc />
    /// <remarks>
    /// MySQL 没有原生的顺序 UUID 函数，使用标准 UUID()
    /// MySQL 8.0+ 可以使用 UUID_TO_BIN(UUID(), 1) 来获取更适合索引的 UUID
    /// </remarks>
    public string NewSequentialGuid => "UUID()";

    #endregion

    #region 布尔值常量

    /// <inheritdoc />
    public string True => "TRUE";

    /// <inheritdoc />
    public string False => "FALSE";

    #endregion

    #region 数学函数

    /// <inheritdoc />
    public string Random => "RAND()";

    #endregion
}
