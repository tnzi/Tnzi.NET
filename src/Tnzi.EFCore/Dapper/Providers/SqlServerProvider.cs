
namespace Tnzi.EFCore.Dapper.Providers;

/// <summary>
/// SQL Server 数据库提供者
/// </summary>
public class SqlServerProvider : IDatabaseProvider
{
    public string DatabaseType => "SqlServer";

    /// <summary>
    /// SQL Server 每命令 2100 个参数，其中 sp_executesql 自己占两个（语句文本与参数声明），
    /// 留给用户参数的是 2098。
    /// </summary>
    public int MaxParametersPerCommand => 2098;

    public string EscapeIdentifier(string identifier)
    {
        return $"[{identifier}]";
    }

    public string ApplyPaging(string sql, int offset, int limit)
    {
        // SQL Server 2012+ 使用 OFFSET ... ROWS FETCH NEXT ... ROWS
        return $"{sql} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
    }

    public string ApplyReturningId(string sql, string keyColumn)
    {
        // SQL Server 使用 SCOPE_IDENTITY()
        return $"{sql}; SELECT SCOPE_IDENTITY()";
    }

    public IDbConnection CreateConnection(string connectionString)
    {
        return new Microsoft.Data.SqlClient.SqlConnection(connectionString);
    }
}