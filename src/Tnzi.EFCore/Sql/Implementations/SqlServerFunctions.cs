namespace Tnzi.EFCore.Sql.Implementations;

/// <summary>
/// SQL Server 数据库的 SQL 函数实现
/// </summary>
internal sealed class SqlServerFunctions : ISqlFunctions
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static readonly SqlServerFunctions Instance = new();

    private SqlServerFunctions() { }

    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    #region 日期时间函数

    /// <inheritdoc />
    public string CurrentUtcTime => "GETUTCDATE()";

    /// <inheritdoc />
    public string CurrentLocalTime => "GETDATE()";

    /// <inheritdoc />
    public string CurrentUtcDate => "CAST(GETUTCDATE() AS DATE)";

    /// <inheritdoc />
    public string CurrentLocalDate => "CAST(GETDATE() AS DATE)";

    /// <inheritdoc />
    public string CurrentTimestamp => "SYSDATETIME()";

    #endregion

    #region GUID/UUID 函数

    /// <inheritdoc />
    public string NewGuid => "NEWID()";

    /// <inheritdoc />
    public string NewSequentialGuid => "NEWSEQUENTIALID()";

    #endregion

    #region 布尔值常量

    /// <inheritdoc />
    public string True => "1";

    /// <inheritdoc />
    public string False => "0";

    #endregion

    #region 数学函数

    /// <inheritdoc />
    public string Random => "RAND()";

    #endregion
}
