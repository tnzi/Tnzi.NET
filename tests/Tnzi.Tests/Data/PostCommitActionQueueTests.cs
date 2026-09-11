namespace Tnzi.Tests.Data;

/// <summary>
/// post-commit 队列的失败隔离回归测试。
///
/// 队列里装的是事务提交后才该发生的副作用（延迟的事件发布、缓存失效等），彼此独立。
/// 逐个 await 而不接异常时，第 k 个动作抛出会让 k+1..N **原地滞留在队列里** ——
/// 它们既没执行也没被丢弃，随后可能被同一作用域内下一个事务的回滚顺手清掉，
/// 也可能在下一次提交时以完全无关的时序被执行。
/// </summary>
public class PostCommitActionQueueTests
{
    [Fact]
    public async Task ExecuteAsync_WhenOneActionThrows_StillRunsTheRest()
    {
        var queue = new PostCommitActionQueue();
        var executed = new List<int>();

        queue.Enqueue(_ => throw new InvalidOperationException("first fails"));
        queue.Enqueue(_ => { executed.Add(2); return Task.CompletedTask; });
        queue.Enqueue(_ => { executed.Add(3); return Task.CompletedTask; });

        await Assert.ThrowsAsync<AggregateException>(() => queue.ExecuteAsync());

        Assert.Equal(new[] { 2, 3 }, executed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionsThrow_DrainsTheQueue()
    {
        var queue = new PostCommitActionQueue();
        queue.Enqueue(_ => throw new InvalidOperationException("boom"));
        queue.Enqueue(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<AggregateException>(() => queue.ExecuteAsync());

        // 队列必须空：残留动作会被下一个事务的回滚清掉，或在无关的时刻被执行
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesEveryFailure()
    {
        var queue = new PostCommitActionQueue();
        queue.Enqueue(_ => throw new InvalidOperationException("first"));
        queue.Enqueue(_ => Task.CompletedTask);
        queue.Enqueue(_ => throw new NotSupportedException("third"));

        var aggregate = await Assert.ThrowsAsync<AggregateException>(() => queue.ExecuteAsync());

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Contains(aggregate.InnerExceptions, e => e is InvalidOperationException);
        Assert.Contains(aggregate.InnerExceptions, e => e is NotSupportedException);
        // 消息要带「几个失败 / 共几个」，否则日志里读不出失败是个别还是全体
        Assert.Contains("2 of 3", aggregate.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllSucceed_DoesNotThrowAndDrainsTheQueue()
    {
        var queue = new PostCommitActionQueue();
        var executed = 0;
        queue.Enqueue(_ => { executed++; return Task.CompletedTask; });
        queue.Enqueue(_ => { executed++; return Task.CompletedTask; });

        await queue.ExecuteAsync();

        Assert.Equal(2, executed);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ExecuteAsync_PassesTheCancellationTokenToEveryAction()
    {
        using var cts = new CancellationTokenSource();
        var queue = new PostCommitActionQueue();
        var seen = new List<CancellationToken>();

        queue.Enqueue(ct => { seen.Add(ct); return Task.CompletedTask; });
        queue.Enqueue(ct => { seen.Add(ct); return Task.CompletedTask; });

        await queue.ExecuteAsync(cts.Token);

        Assert.All(seen, token => Assert.Equal(cts.Token, token));
    }
}
