using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tnzi.AI.Tools.Sql;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Tools.Sql;

public class ReadOnlySqlExecutorTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly IOptions<SqlToolOptions> _toolOptions =
        MsOptions.Create(new SqlToolOptions { AllowNonTSqlDialects = true, DefaultDialect = SqlDialect.Sqlite });

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

    private ReadOnlySqlExecutor CreateSut(IReadOnlySqlPermissionCheck? permissionCheck = null)
    {
        Func<string?, DbConnection> factory = _ =>
            new SqliteConnection("Data Source=ExecutorTest;Mode=Memory;Cache=Shared");
        return new ReadOnlySqlExecutor(
            new RestrictiveSqlValidator(_toolOptions),
            factory,
            permissionCheck ?? new AllowAllPermissionCheck(),
            _toolOptions,
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
        var sut = CreateSut(new DenyPermissionCheck());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ExecuteAsync("SELECT * FROM t"));
    }

    [Fact]
    public async Task Execute_DefaultPermissionCheck_DenyAll_Throws()
    {
        var sut = CreateSut(new DenyAllSqlPermissionCheck());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ExecuteAsync("SELECT * FROM t"));
    }

    [Fact]
    public async Task Execute_RejectsConnectionNotInWhitelist()
    {
        var opts = MsOptions.Create(new SqlToolOptions
        {
            AllowNonTSqlDialects = true,
            DefaultDialect = SqlDialect.Sqlite,
            AllowedConnectionNames = new[] { "audit-readonly" }
        });
        Func<string?, DbConnection> factory = _ =>
            new SqliteConnection("Data Source=ExecutorTest;Mode=Memory;Cache=Shared");
        var sut = new ReadOnlySqlExecutor(
            new RestrictiveSqlValidator(opts),
            factory,
            new AllowAllPermissionCheck(),
            opts,
            NullLogger<ReadOnlySqlExecutor>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.ExecuteAsync("SELECT * FROM t"));
    }

    [Fact]
    public async Task Execute_ClampsMaxRowsToCeiling()
    {
        var opts = MsOptions.Create(new SqlToolOptions
        {
            AllowNonTSqlDialects = true,
            DefaultDialect = SqlDialect.Sqlite,
            DefaultMaxRows = 2,
            MaxAllowedMaxRows = 4
        });
        Func<string?, DbConnection> factory = _ =>
            new SqliteConnection("Data Source=ExecutorTest;Mode=Memory;Cache=Shared");
        var sut = new ReadOnlySqlExecutor(
            new RestrictiveSqlValidator(opts),
            factory,
            new AllowAllPermissionCheck(),
            opts,
            NullLogger<ReadOnlySqlExecutor>.Instance);

        // Caller asks for 1000 → should be clamped to 4
        var result = await sut.ExecuteAsync("SELECT * FROM t", new ReadOnlySqlExecutionOptions(MaxRows: 1000));
        Assert.Equal(4, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    private sealed class DenyPermissionCheck : IReadOnlySqlPermissionCheck
    {
        public Task<SqlPermissionResult> CheckAsync(string sql, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new SqlPermissionResult(false, "denied by policy"));
    }

    private sealed class AllowAllPermissionCheck : IReadOnlySqlPermissionCheck
    {
        public Task<SqlPermissionResult> CheckAsync(string sql, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new SqlPermissionResult(true, null));
    }
}
