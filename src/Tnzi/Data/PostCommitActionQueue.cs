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

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        while (_actions.Count > 0)
        {
            var action = _actions.Dequeue();
            await action(cancellationToken);
        }
    }

    public void Clear() => _actions.Clear();

    public int Count => _actions.Count;
}
