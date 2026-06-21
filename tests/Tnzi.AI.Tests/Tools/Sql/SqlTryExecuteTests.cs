using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tnzi.AI.Tools.Sql;
using Xunit;

namespace Tnzi.AI.Tests.Tools.Sql;

/// <summary>
/// F5: text-to-SQL error model — QueryResult.Failure + IReadOnlySqlExecutor.TryExecuteAsync
/// (failure becomes a failed result instead of throwing, so callers can tell
/// "no rows" from "rejected/timed out").
/// </summary>
public class SqlTryExecuteTests
{
    private sealed class FakeExecutor : IReadOnlySqlExecutor
    {
        private readonly Exception? _throw;
        public FakeExecutor(Exception? ex = null) => _throw = ex;

        public Task<QueryResult> ExecuteAsync(string sql, ReadOnlySqlExecutionOptions? options = null, CancellationToken ct = default)
        {
            if (_throw is not null) throw _throw;
            return Task.FromResult(new QueryResult(
                new[] { new QueryColumn("id", "Int32") },
                new IReadOnlyList<object?>[] { new object?[] { 1 } },
                Truncated: false, DurationMs: 5, ExecutedSql: sql));
        }
    }

    [Fact]
    public void Failure_CarriesErrorAndIsNotSuccess()
    {
        var r = QueryResult.Failure("boom", "SELECT 1");

        Assert.False(r.IsSuccess);
        Assert.Equal("boom", r.ErrorMessage);
        Assert.Equal("SELECT 1", r.ExecutedSql);
        Assert.Empty(r.Rows);
        Assert.Empty(r.Columns);
    }

    [Fact]
    public async Task TryExecute_OnSuccess_ReturnsSuccessfulResult()
    {
        IReadOnlySqlExecutor sut = new FakeExecutor();

        var r = await sut.TryExecuteAsync("SELECT id FROM t");

        Assert.True(r.IsSuccess);
        Assert.Null(r.ErrorMessage);
        Assert.Single(r.Rows);
    }

    [Fact]
    public async Task TryExecute_OnFailure_ReturnsFailedResultWithError()
    {
        IReadOnlySqlExecutor sut = new FakeExecutor(new InvalidOperationException("SQL validation failed: DML"));

        var r = await sut.TryExecuteAsync("DELETE FROM t");

        Assert.False(r.IsSuccess);
        Assert.Contains("validation failed", r.ErrorMessage);
        Assert.Empty(r.Rows);
        Assert.Equal("DELETE FROM t", r.ExecutedSql);
    }

    [Fact]
    public async Task TryExecute_OnCancellation_StillThrows()
    {
        IReadOnlySqlExecutor sut = new FakeExecutor(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.TryExecuteAsync("SELECT 1"));
    }

    [Fact]
    public async Task TryExecute_OnTaskCanceled_StillThrows()
    {
        // TaskCanceledException (what reader.ReadAsync(ct) throws) is an
        // OperationCanceledException subclass — must propagate, not become Failure.
        IReadOnlySqlExecutor sut = new FakeExecutor(new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => sut.TryExecuteAsync("SELECT 1"));
    }
}
