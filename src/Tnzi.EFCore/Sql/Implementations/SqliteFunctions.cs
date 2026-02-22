namespace Tnzi.EFCore.Sql.Implementations;

/// <summary>
/// SQLite 数据库的 SQL 函数实现
/// </summary>
internal sealed class SqliteFunctions : ISqlFunctions
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static readonly SqliteFunctions Instance = new();

    private SqliteFunctions() { }

    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    #region 日期时间函数

    /// <inheritdoc />
    public string CurrentUtcTime => "DATETIME('now')";

    /// <inheritdoc />
    public string CurrentLocalTime => "DATETIME('now', 'localtime')";

    /// <inheritdoc />
    public string CurrentUtcDate => "DATE('now')";

    /// <inheritdoc />
    public string CurrentLocalDate => "DATE('now', 'localtime')";

    /// <inheritdoc />
    public string CurrentTimestamp => "STRFTIME('%Y-%m-%d %H:%M:%f', 'now')";

    #endregion

    #region GUID/UUID 函数

    /// <inheritdoc />
    /// <remarks>
    /// SQLite 没有原生的 UUID 函数，使用 RANDOMBLOB 生成符合 UUID v4 规范的值
    /// 格式：xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx
    /// </remarks>
    public string NewGuid =>
        "(LOWER(HEX(RANDOMBLOB(4))) || '-' || " +
        "LOWER(HEX(RANDOMBLOB(2))) || '-4' || " +
        "SUBSTR(LOWER(HEX(RANDOMBLOB(2))), 2) || '-' || " +
        "SUBSTR('89ab', ABS(RANDOM()) % 4 + 1, 1) || " +
        "SUBSTR(LOWER(HEX(RANDOMBLOB(2))), 2) || '-' || " +
        "LOWER(HEX(RANDOMBLOB(6))))";

    /// <inheritdoc />
    /// <remarks>
    /// SQLite 没有原生的顺序 UUID 支持，使用与 NewGuid 相同的实现
    /// </remarks>
    public string NewSequentialGuid =>
        "(LOWER(HEX(RANDOMBLOB(4))) || '-' || " +
        "LOWER(HEX(RANDOMBLOB(2))) || '-4' || " +
        "SUBSTR(LOWER(HEX(RANDOMBLOB(2))), 2) || '-' || " +
        "SUBSTR('89ab', ABS(RANDOM()) % 4 + 1, 1) || " +
        "SUBSTR(LOWER(HEX(RANDOMBLOB(2))), 2) || '-' || " +
        "LOWER(HEX(RANDOMBLOB(6))))";

    #endregion

    #region 布尔值常量

    /// <inheritdoc />
    public string True => "1";

    /// <inheritdoc />
    public string False => "0";

    #endregion

    #region 数学函数

    /// <inheritdoc />
    /// <remarks>
    /// SQLite 的 RANDOM() 返回 -9223372036854775808 到 +9223372036854775807 之间的整数
    /// 转换为 0 到 1 之间的浮点数
    /// </remarks>
    public string Random => "(ABS(RANDOM()) / CAST(9223372036854775807 AS REAL))";

    #endregion
}
