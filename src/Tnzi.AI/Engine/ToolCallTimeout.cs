namespace Tnzi.AI.Engine;

/// <summary>
/// 一次工具调用的超时预算：<see cref="AgentExecutor"/> 调用工具时建立，到点取消交给工具的令牌。
/// </summary>
/// <remarks>
/// <para>
/// 预算衡量的是工具自己在干活的时间。有些等待本来就有自己的、更明确的上限：等人审批（<c>AI:ToolApproval:TimeoutSeconds</c>）、
/// 子 Agent 自带超时（<c>call_*</c> 的 <c>SubAgentTimeout</c>）、调用方指定了等待时长的 <c>wait_agent</c>。
/// 这些等待经 <see cref="Suspend"/> 暂停预算，否则固定的工具超时会在它们自己的上限之前把它们截断，
/// 配置写的 300 秒审批窗口实际只有工具超时那么长。
/// </para>
/// <para>
/// 预算随执行流（AsyncLocal）传给工具；工具里再起的 Agent 调用自己的工具时会建立自己的预算，互不干扰。
/// 不在工具调用里（没有当前预算）时 <see cref="Suspend"/> 什么也不做。
/// </para>
/// </remarks>
internal sealed class ToolCallTimeout
{
    private static readonly AsyncLocal<ToolCallTimeout?> CurrentHolder = new();

    private readonly CancellationTokenSource _cts;
    private readonly object _gate = new();
    private DateTime _deadlineUtc;
    private TimeSpan _remainingWhileSuspended;
    private int _suspensions;

    private ToolCallTimeout(CancellationTokenSource cts, TimeSpan budget)
    {
        _cts = cts;
        _deadlineUtc = DateTime.UtcNow + budget;
    }

    /// <summary>
    /// 在当前执行流上建立一次工具调用的预算，到点取消 <paramref name="cts"/>。返回值释放时撤销（不取消）。
    /// </summary>
    internal static IDisposable Begin(CancellationTokenSource cts, TimeSpan budget)
    {
        Check.NotNull(cts);
        var previous = CurrentHolder.Value;
        CurrentHolder.Value = new ToolCallTimeout(cts, budget);
        cts.CancelAfter(budget);
        return new Restore(previous);
    }

    /// <summary>
    /// 暂停当前工具调用的超时预算，直到返回值被释放；暂停期间流逝的时间不计入预算。可嵌套。
    /// </summary>
    internal static IDisposable Suspend()
    {
        var current = CurrentHolder.Value;
        if (current == null)
        {
            return NoOpScope.Instance;
        }

        current.Pause();
        return new ResumeScope(current);
    }

    private void Pause()
    {
        lock (_gate)
        {
            if (_suspensions++ > 0)
            {
                return;
            }

            _remainingWhileSuspended = _deadlineUtc - DateTime.UtcNow;
            if (_remainingWhileSuspended <= TimeSpan.Zero)
            {
                // 已经到点（或计时器正要触发）：不暂停一个已经用完的预算
                return;
            }

            TrySetTimer(Timeout.InfiniteTimeSpan);
        }
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (--_suspensions > 0 || _remainingWhileSuspended <= TimeSpan.Zero)
            {
                return;
            }

            _deadlineUtc = DateTime.UtcNow + _remainingWhileSuspended;
            TrySetTimer(_remainingWhileSuspended);
        }
    }

    private void TrySetTimer(TimeSpan delay)
    {
        try
        {
            _cts.CancelAfter(delay);
        }
        catch (ObjectDisposedException)
        {
            // 工具调用已经收尾（执行器释放了令牌源），预算不再有意义
        }
    }

    private sealed class Restore(ToolCallTimeout? previous) : IDisposable
    {
        public void Dispose() => CurrentHolder.Value = previous;
    }

    private sealed class ResumeScope(ToolCallTimeout owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Resume();
            }
        }
    }

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();

        public void Dispose()
        {
        }
    }
}
