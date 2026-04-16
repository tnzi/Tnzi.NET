using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Tools.Sql;
using Xunit;

namespace Tnzi.AI.Tests.Tools.Sql;

public class ReadOnlySqlExecutorTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public ReadOnlySqlExecutorTests()
    {
        // Shared in-memory DB via named connection string — keep-alive conn holds it open
        _keepAlive = new SqliteConnection("Data Source=ExecutorTest;Mode=Memory;Cache=Shared");
        _keepAlive.Open();
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT);
            INSERT INTO t VALUES (1,'a'),(2,'b'),(3,'c'),(4,'d'),(5,'e');";
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _keepAlive.Dispose();

    private ReadOnlySqlExecutor CreateSut()
    {
        Func<string?, DbConnection> factory = _ =>
            new SqliteConnection("Data Source=ExecutorTest;Mode=Memory;Cache=Shared");
        return new ReadOnlySqlExecutor(
            new RestrictiveSqlValidator(),
            factory,
            NullLogger<ReadOnlySqlExecutor>.Instance);
    }

    [Fact]
    public async Task Execute_ReturnsColumnsAndRows()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync("SELECT id, name FROM t ORDER BY id");
        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("id", result.Columns[0].Name);
        Assert.Equal(5, result.Rows.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Execute_RejectsNonSelect()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ExecuteAsync("DELETE FROM t"));
    }

    [Fact]
    public async Task Execute_TruncatesAtMaxRows()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync("SELECT * FROM t", new ReadOnlySqlExecutionOptions(MaxRows: 3));
        Assert.Equal(3, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Execute_PermissionDeniedThrows()
    {
        Func<string?, DbConnection> factory = _ =>
            new SqliteConnection("Data Source=ExecutorTest;Mode=Memory;Cache=Shared");
        var denyCheck = new DenyPermissionCheck();
        var sut = new ReadOnlySqlExecutor(
            new RestrictiveSqlValidator(),
            factory,
            NullLogger<ReadOnlySqlExecutor>.Instance,
            denyCheck);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ExecuteAsync("SELECT * FROM t"));
    }

    private sealed class DenyPermissionCheck : IReadOnlySqlPermissionCheck
    {
        public Task<SqlPermissionResult> CheckAsync(string sql, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new SqlPermissionResult(false, "denied by policy"));
    }
}
