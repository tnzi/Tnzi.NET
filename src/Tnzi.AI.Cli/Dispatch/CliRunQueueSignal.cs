namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 让本进程的队列循环提前醒来的提示：有一个远程执行目标刚上线、或者刚入队了一条要唤起目标的运行。
/// </summary>
/// <remarks>
/// 只是提示：丢失、重复都安全，队列循环照样按 <c>AI:Cli:PollInterval</c> 轮询。它让「目标上线 → 认领」不必再等一个轮询间隔。
/// 多副本下只叫醒本进程。
/// </remarks>
public sealed class CliRunQueueSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>叫醒队列循环（已经叫过、还没被取走时什么都不做）。</summary>
    public void Nudge()
    {
        try
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // 两个叫醒恰好同时到达：已经有一个在等着被取走，够了
        }
    }

    /// <summary>等到被叫醒或超时。</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}
