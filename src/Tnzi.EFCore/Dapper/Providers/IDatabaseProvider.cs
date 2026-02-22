
namespace Tnzi.EFCore.Dapper.Providers;


/// <summary>
/// 数据库提供者接口（用于处理不同数据库的 SQL 语法差异）
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>
    /// 数据库类型名称
    /// </summary>
    string DatabaseType { get; }

    /// <summary>
    /// 转义标识符（表名、列名等）
    /// </summary>
    /// <param name="identifier">标识符</param>
    /// <returns>转义后的标识符</returns>
    string EscapeIdentifier(string identifier);

    /// <summary>
    /// 生成分页 SQL（OFFSET/FETCH 或 LIMIT/OFFSET）
    /// </summary>
    /// <param name="sql">原始 SQL</param>
    /// <param name="offset">偏移量</param>
    /// <param name="limit">限制数量</param>
    /// <returns>分页 SQL</returns>
    string ApplyPaging(string sql, int offset, int limit);

    /// <summary>
    /// 生成返回主键的 SQL（用于 INSERT 后获取生成的主键）
    /// </summary>
    /// <param name="sql">INSERT SQL</param>
    /// <param name="keyColumn">主键列名</param>
    /// <returns>包含返回主键的 SQL</returns>
    string ApplyReturningId(string sql, string keyColumn);

    /// <summary>
    /// 创建数据库连接
    /// </summary>
    /// <param name="connectionString">连接字符串</param>
    /// <returns>数据库连接</returns>
    IDbConnection CreateConnection(string connectionString);
}