using System.Reflection;
using Tnzi.EFCore.Dapper;
using Tnzi.EFCore.Dapper.Providers;

namespace Tnzi.EFCore.Tests.Dapper;

/// <summary>
/// 批量更新在每种方言下都只更新已存在的行：不存在的主键不会被插入，返回值是真实更新的行数。
/// </summary>
/// <remarks>
/// <para>
/// ★ MySQL 此前走 <c>INSERT ... ON DUPLICATE KEY UPDATE</c>：并发删掉的行（或调用方传错的主键）被整行重新插入，
/// 而 SQL Server / PostgreSQL / 回退方言的 <c>UPDATE ... FROM</c> 从不插入；它还按每行 2 计受影响行数，
/// 同一个公开 API 的返回值只在 MySQL 上翻倍。
/// </para>
/// <para>
/// CI 里没有 MySQL，断言落在生成的 SQL 上（方言选择是私有方法，经反射调用）。
/// </para>
/// </remarks>
public class DapperBulkUpdateSqlTests
{
    private static readonly List<string> Columns = ["Name", "Price"];

    [Fact]
    public void MySql_BulkUpdate_IsAnUpdateThatNeverInserts()
    {
        var sql = Generate(new MySqlProvider(), entityCount: 2);

        Assert.StartsWith("UPDATE `Products` AS t", sql);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DUPLICATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON t.`Id` = v.`key_val`", sql);
        Assert.Contains("SET t.`Name` = v.`val0`, t.`Price` = v.`val1`", sql);
    }

    [Fact]
    public void MySql_BulkUpdate_BindsEveryRowOnce()
    {
        var sql = Generate(new MySqlProvider(), entityCount: 3);

        // 列别名只写在第一行，后续行按位置对齐
        Assert.Contains("SELECT @Id_0 AS `key_val`, @Name_0 AS `val0`, @Price_0 AS `val1`", sql);
        Assert.Contains("UNION ALL SELECT @Id_1, @Name_1, @Price_1", sql);
        Assert.Contains("UNION ALL SELECT @Id_2, @Name_2, @Price_2", sql);
        foreach (var parameter in new[] { "@Id_0", "@Name_1", "@Price_2" })
        {
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, $@"{parameter}\b"));
        }
    }

    [Fact]
    public void OtherDialects_BulkUpdate_StayUpdateFrom()
    {
        Assert.StartsWith("UPDATE [Products]", Generate(new SqlServerProvider(), entityCount: 1));
        Assert.StartsWith("UPDATE \"Products\"", Generate(new PostgreSQLProvider(), entityCount: 1));
    }

    private static string Generate(IDatabaseProvider provider, int entityCount)
    {
        var method = typeof(DapperBulkOperations).GetMethod("GenerateBulkUpdateSql", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DapperBulkOperations.GenerateBulkUpdateSql was not found.");

        return (string)method.Invoke(null,
        [
            provider,
            provider.EscapeIdentifier("Products"),
            provider.EscapeIdentifier("Id"),
            "Id",
            Columns,
            entityCount,
        ])!;
    }
}
