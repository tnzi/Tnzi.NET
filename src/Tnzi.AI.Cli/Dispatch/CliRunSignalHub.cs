namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 进程内的运行信号中枢：唤醒等待中的订阅者与队列处理器。
/// </summary>
/// <remarks>
/// <para>
/// <b>信号是提示，不是数据。</b>它只说「这条运行有新东西了，去查数据库」，
/// 从不携带业务负载。丢一次信号最多让订阅者晚一个轮询周期看到事件，不会丢事件；
/// 重复、乱序同样安全。正确性完全押在数据库上。
/// </para>
/// <para>
/// 这条纪律不是洁癖：多副本部署下，订阅者与执行者很可能不在同一个进程里，
/// 此时进程内信号根本到不了对方 —— 于是「信号只是提示」是唯一能同时适配单副本与多副本的语义。
/// </para>
/// </remarks>
public class CliRunSignalHub
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _waiters = new();

    /// <summary>唤醒等待某条运行的订阅者。</summary>
    public void Signal(Guid runId)
    {
        if (_waiters.TryRemove(runId, out var waiter))
        {
            waiter.TrySetResult();
        }
    }

    /// <summary>
    /// 等待某条运行的下一个信号，或等到超时。
    /// </summary>
    /// <remarks>
    /// 超时返回是正常路径而不是异常：调用方本来就要在超时后重新查库
    /// （信号可能来自别的副本，永远等不到）。
    /// </remarks>
    public async Task WaitAsync(Guid runId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var waiter = _waiters.GetOrAdd(runId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, cts.Token);
        var completed = await Task.WhenAny(waiter.Task, delay);
        if (completed == waiter.Task)
        {
            await cts.CancelAsync();
        }
        else
        {
            _waiters.TryRemove(runId, out _);
        }
    }
}

/// <summary>
/// 进程内的运行取消登记处。
/// </summary>
/// <remarks>
/// 取消请求同时写库（<c>CliRun.CancelRequested</c>）与登记到这里：
/// 前者是跨副本的权威事实，后者让<b>本副本正在跑的</b>那条运行能立刻响应，
/// 而不是等到下一个轮询周期 —— 一个正在烧模型预算的进程，多跑三秒都是钱。
/// </remarks>
public class CliRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    /// <summary>登记一条正在执行的运行，返回可用于中止它的令牌源。</summary>
    public CancellationTokenSource Register(Guid runId, CancellationToken linkedTo)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedTo);
        _running[runId] = cts;
        return cts;
    }

    /// <summary>注销一条已结束的运行。</summary>
    public void Unregister(Guid runId)
    {
        if (_running.TryRemove(runId, out var cts))
        {
            cts.Dispose();
        }
    }

    /// <summary>请求中止一条本地正在跑的运行；不在本副本则返回 false。</summary>
    public bool TryCancel(Guid runId)
    {
        if (!_running.TryGetValue(runId, out var cts))
        {
            return false;
        }

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // 运行刚好自己结束了，取消已无意义。
            return false;
        }
    }
}
