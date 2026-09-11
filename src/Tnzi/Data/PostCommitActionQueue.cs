namespace Tnzi.Data;

/// <summary>
/// 事务提交后操作队列实现
/// </summary>
public class PostCommitActionQueue : IPostCommitActionQueue
{
    private readonly Queue<Func<CancellationToken, Task>> _actions = new();

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        Check.NotNull(action);
        _actions.Enqueue(action);
    }

    /// <summary>
    /// 依次执行队列中的所有操作并清空队列。
    /// </summary>
    /// <remarks>
    /// 队列里的动作是**彼此独立**的提交后副作用（延迟的事件发布、缓存失效等），
    /// 一个失败不构成跳过其余的理由。逐个 await 而不接异常时，第 k 个抛出会让 k+1..N
    /// 原地滞留在队列里：既没执行也没被丢弃，随后被同一作用域内下一个事务的回滚顺手清掉，
    /// 或在完全无关的时刻被执行。因此这里逐个隔离失败、全部跑完，
    /// 再把收集到的异常合并成一个 <see cref="AggregateException"/> 抛出
    /// （消息里带「几个失败 / 共几个」，否则日志读不出失败是个别还是全体）。
    /// 事务已经提交，调用方通常只应记录而不应据此失败。
    /// </remarks>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        List<Exception>? failures = null;

        while (_actions.Count > 0)
        {
            var action = _actions.Dequeue();
            total++;

            try
            {
                await action(cancellationToken);
            }
            catch (Exception ex)
            {
                (failures ??= new()).Add(ex);
            }
        }

        if (failures != null)
        {
            throw new AggregateException(
                $"{failures.Count} of {total} post-commit actions failed.", failures);
        }
    }

    public void Clear() => _actions.Clear();

    public int Count => _actions.Count;
}
