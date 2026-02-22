
namespace Tnzi.EFCore.Dapper.Providers;

/// <summary>
/// SQL Server 数据库提供者
/// </summary>
public class SqlServerProvider : IDatabaseProvider
{
    public string DatabaseType => "SqlServer";

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