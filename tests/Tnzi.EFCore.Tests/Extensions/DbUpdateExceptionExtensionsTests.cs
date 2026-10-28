using System.Data.Common;
using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests.Extensions;

/// <summary>
/// 唯一约束冲突按驱动的错误码识别，不靠英文消息文本。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 此前只在内层消息里找 "UNIQUE constraint" / "duplicate key" / "unique constraint"：
/// MySQL 报的是 <c>Duplicate entry 'x' for key 'IX_...'</c>（一个都不中），SQL Server 与 PostgreSQL 的消息随服务器语言本地化。
/// 漏判时六十个调用点把本该 409 的重复编码答成 500，单据编号首插的并发重试直接失败。
/// </para>
/// <para>
/// 测试项目只引用 SQLite 驱动：SQL Server / MySQL 的异常用同名替身（识别按类型名 + 属性读取，不引用驱动包），
/// PostgreSQL 走 <see cref="DbException.SqlState"/>。
/// </para>
/// </remarks>
public class DbUpdateExceptionExtensionsTests
{
    [Fact]
    public void PostgreSql_UniqueViolation_IsRecognisedBySqlStateWhateverTheLanguage()
    {
        var inner = new SqlStateException("23505", "FEHLER: doppelter Schlüsselwert verletzt Unique-Constraint");

        Assert.True(Wrap(inner).IsUniqueConstraintViolation());
    }

    [Fact]
    public void PostgreSql_OtherConstraint_IsNotAUniqueViolation()
    {
        var inner = new SqlStateException("23503", "insert or update on table violates foreign key constraint");

        Assert.False(Wrap(inner).IsUniqueConstraintViolation());
    }

    [Theory]
    [InlineData(1062)]
    [InlineData(1586)]
    public void MySql_DuplicateEntry_IsRecognisedByErrorNumber(int number)
    {
        var inner = new MySqlException(number, "Duplicate entry 'ACME' for key 'IX_Customer_Code'");

        Assert.True(Wrap(inner).IsUniqueConstraintViolation());
    }

    [Fact]
    public void MySql_ForeignKeyFailure_IsNotAUniqueViolation()
    {
        var inner = new MySqlException(1452, "Cannot add or update a child row: a foreign key constraint fails");

        Assert.False(Wrap(inner).IsUniqueConstraintViolation());
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void SqlServer_UniqueViolation_IsRecognisedByErrorNumberWhateverTheLanguage(int number)
    {
        var inner = new SqlException(number, "Violation de la contrainte UNIQUE KEY « UQ_Customer_Code ».");

        Assert.True(Wrap(inner).IsUniqueConstraintViolation());
    }

    [Fact]
    public void SqlServer_ForeignKeyFailure_IsNotAUniqueViolation()
    {
        var inner = new SqlException(547, "The INSERT statement conflicted with the FOREIGN KEY constraint");

        Assert.False(Wrap(inner).IsUniqueConstraintViolation());
    }

    /// <summary>真实 SQLite：唯一索引冲突识别为真，外键冲突识别为假。</summary>
    [Fact]
    public async Task Sqlite_RealViolations_AreClassifiedByExtendedErrorCode()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
        await ExecuteAsync(connection, "CREATE TABLE Parent (Id INTEGER PRIMARY KEY, Code TEXT UNIQUE);");
        await ExecuteAsync(connection, "CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER REFERENCES Parent(Id));");
        await ExecuteAsync(connection, "INSERT INTO Parent (Id, Code) VALUES (1, 'a');");

        var unique = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO Parent (Id, Code) VALUES (2, 'a');"));
        var foreignKey = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO Child (Id, ParentId) VALUES (1, 99);"));

        Assert.True(Wrap(unique).IsUniqueConstraintViolation());
        Assert.False(Wrap(foreignKey).IsUniqueConstraintViolation());
    }

    [Fact]
    public void NoInnerException_IsNotAUniqueViolation()
        => Assert.False(new DbUpdateException("failed").IsUniqueConstraintViolation());

    private static DbUpdateException Wrap(Exception inner) => new("An error occurred while saving the entity changes.", inner);

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>只报 SQLSTATE 的驱动异常（Npgsql 的 PostgresException 就是这样覆写 <see cref="DbException.SqlState"/>）。</summary>
    private sealed class SqlStateException(string sqlState, string message) : DbException(message)
    {
        public override string? SqlState => sqlState;
    }

    /// <summary>与 MySqlConnector / MySql.Data 的异常同名同属性（<c>Number</c>）。</summary>
    private sealed class MySqlException(int number, string message) : DbException(message)
    {
        public int Number { get; } = number;
    }

    /// <summary>与 Microsoft.Data.SqlClient 的异常同名同属性（<c>Number</c>）。</summary>
    private sealed class SqlException(int number, string message) : DbException(message)
    {
        public int Number { get; } = number;
    }
}
