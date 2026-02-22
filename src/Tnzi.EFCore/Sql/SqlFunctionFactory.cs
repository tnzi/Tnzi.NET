
namespace Tnzi.EFCore.Sql;

/// <summary>
/// SQL 函数工厂，提供不同数据库的 SQL 函数实现
/// </summary>
/// <remarks>
/// 此工厂类提供了获取数据库特定 SQL 函数的统一入口。
/// 使用此工厂可以避免在实体配置中硬编码数据库特定的 SQL 语法。
/// <para>
/// 使用示例：
/// <code>
/// // 方式1：获取完整的函数提供者
/// var sql = SqlFunctionFactory.GetFunctions(DatabaseProvider.PostgreSQL);
/// builder.Property(e => e.CreatedAt).HasDefaultValueSql(sql.CurrentUtcTime);
/// 
/// // 方式2：使用快捷方法
/// builder.Property(e => e.CreatedAt)
///     .HasDefaultValueSql(SqlFunctionFactory.CurrentUtcTime(DatabaseProvider.PostgreSQL));
/// </code>
/// </para>
/// <para>
/// 重要：请勿在实体配置中硬编码数据库特定的 SQL 函数（如 GETUTCDATE()、NOW() 等），
/// 应始终使用此工厂或 PropertyBuilder 扩展方法来确保跨数据库兼容性。
/// </para>
/// </remarks>
public static class SqlFunctionFactory
{
    private static readonly FrozenDictionary<DatabaseProvider, ISqlFunctions> _functions;

    static SqlFunctionFactory()
    {
        _functions = new Dictionary<DatabaseProvider, ISqlFunctions>
        {
            [DatabaseProvider.SqlServer] = SqlServerFunctions.Instance,
            [DatabaseProvider.PostgreSQL] = PostgreSqlFunctions.Instance,
            [DatabaseProvider.MySql] = MySqlFunctions.Instance,
            [DatabaseProvider.Sqlite] = SqliteFunctions.Instance
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// 获取指定数据库提供者的 SQL 函数实现
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 函数实现</returns>
    /// <exception cref="NotSupportedException">当数据库提供者不支持时抛出</exception>
    public static ISqlFunctions GetFunctions(DatabaseProvider provider)
    {
        if (!_functions.TryGetValue(provider, out var functions))
        {
            throw new NotSupportedException(
                $"Database provider {provider} is not supported. " +
                $"Supported providers: {string.Join(", ", _functions.Keys)}.");
        }

        return functions;
    }

    /// <summary>
    /// 检查是否支持指定的数据库提供者
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>是否支持</returns>
    public static bool IsSupported(DatabaseProvider provider) => _functions.ContainsKey(provider);

    /// <summary>
    /// 获取所有支持的数据库提供者
    /// </summary>
    /// <returns>支持的提供者列表</returns>
    public static IReadOnlyCollection<DatabaseProvider> GetSupportedProviders() => _functions.Keys;

    #region 日期时间函数快捷方法

    /// <summary>
    /// 获取当前 UTC 时间的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string CurrentUtcTime(DatabaseProvider provider) => GetFunctions(provider).CurrentUtcTime;

    /// <summary>
    /// 获取当前本地时间的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string CurrentLocalTime(DatabaseProvider provider) => GetFunctions(provider).CurrentLocalTime;

    /// <summary>
    /// 获取当前 UTC 日期的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string CurrentUtcDate(DatabaseProvider provider) => GetFunctions(provider).CurrentUtcDate;

    /// <summary>
    /// 获取当前本地日期的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string CurrentLocalDate(DatabaseProvider provider) => GetFunctions(provider).CurrentLocalDate;

    /// <summary>
    /// 获取当前时间戳的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string CurrentTimestamp(DatabaseProvider provider) => GetFunctions(provider).CurrentTimestamp;

    #endregion

    #region GUID/UUID 函数快捷方法

    /// <summary>
    /// 获取生成新 GUID 的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string NewGuid(DatabaseProvider provider) => GetFunctions(provider).NewGuid;

    /// <summary>
    /// 获取生成顺序 GUID 的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string NewSequentialGuid(DatabaseProvider provider) => GetFunctions(provider).NewSequentialGuid;

    #endregion

    #region 布尔值常量快捷方法

    /// <summary>
    /// 获取布尔值 True 的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string True(DatabaseProvider provider) => GetFunctions(provider).True;

    /// <summary>
    /// 获取布尔值 False 的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string False(DatabaseProvider provider) => GetFunctions(provider).False;

    #endregion

    #region 数学函数快捷方法

    /// <summary>
    /// 获取随机数函数的 SQL 表达式
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>SQL 表达式</returns>
    public static string Random(DatabaseProvider provider) => GetFunctions(provider).Random;

    #endregion
}