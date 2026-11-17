namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 一个副本上「等后台任务的运行」与「同线程下一条运行」之间的交接处（文档 §6.18）：进程、它的后台任务与并发槽从前者交给后者。
/// </summary>
/// <remarks>
/// <para>
/// 一轮结束而 CLI 还有后台任务（开发服务器、跑着的测试）时，运行保持 Running、进程开着。此前同线程的下一条消息按同线程互斥排在它后面，
/// 一个不会自己结束的后台任务（预览服务器）让对话最多锁住 <c>AI:Cli:BackgroundTaskWait</c>，唯一的出路「停下」又连同服务器一起杀掉。
/// 现在等着的运行在这里登记（<see cref="Offer"/>）；队列处理器认领同线程排着的下一条运行（<see cref="WaitingRuns"/>），
/// 下一条运行过完自己的治理门后来取进程（<see cref="TakeAsync"/>），等着的运行在两轮之间停下、落终态、把进程交出去（<see cref="Hand"/>）。
/// </para>
/// <para>
/// ★ 只在进程内：进程只在执行它的那个副本上，交接也只能在那里发生。别的副本看到的是线程上有一条进行中的运行，照旧不认领。
/// ★ 并发槽跟着进程走（<see cref="HoldSlot"/> / <see cref="ReleaseSlot"/>）：交接前后进程只有一个，占的槽也只有一个。
/// 下一条运行认领时不另占槽，等着的那条结束时把槽交给它：接过进程时如此，进程没能交出（最后一轮恰好收尾了）、它自己起进程时也如此。
/// </para>
/// </remarks>
public sealed class CliRunHandoffs
{
    private readonly ConcurrentDictionary<Guid, CliWaitingRun> _waiting = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _slots = new();
    private readonly Lock _slotGate = new();

    /// <summary>此刻在这个副本上等后台任务、可以交出进程的运行。</summary>
    public IReadOnlyCollection<CliWaitingRun> WaitingRuns() => _waiting.Values.ToList();

    /// <summary>一条运行开始等后台任务：登记它，同线程的下一条运行可以来取它的进程。同一线程只登记一条（后到的顶掉先到的）。</summary>
    internal CliWaitingRun Offer(Guid runId, Guid threadId, Guid agentId, Guid runtimeId)
    {
        var waiting = new CliWaitingRun(runId, threadId, agentId, runtimeId);
        _waiting[threadId] = waiting;
        return waiting;
    }

    /// <summary>
    /// 等着的运行已经落终态（或者出了别的结局而进程已经结束）：把进程交给来取的那条运行（<paramref name="session"/> 为 null = 没有进程可交，
    /// 它自己起进程），连同它占的并发槽。没有人来取、或者来取的那条已经放弃（被停下、宿主停机）时返回 false，进程（有的话）由调用方结束、槽留在调用方名下。
    /// </summary>
    internal bool Hand(CliWaitingRun waiting, CliLiveSession? session)
    {
        _waiting.TryRemove(new KeyValuePair<Guid, CliWaitingRun>(waiting.ThreadId, waiting));
        if (waiting.SuccessorRunId is not { } successor)
        {
            waiting.Delivered.TrySetResult(null);
            return false;
        }

        // 先挪槽再交：接手的运行一拿到就可能跑完、归还它名下的槽
        MoveSlot(waiting.RunId, successor);
        if (waiting.Delivered.TrySetResult(session))
        {
            return true;
        }

        MoveSlot(successor, waiting.RunId);
        return false;
    }

    /// <summary>
    /// 同线程等后台任务的运行在这个副本上时，等它在两轮之间停下、交出进程。没有等着的运行、或者它没能交出（进程已经结束）时返回 null。
    /// </summary>
    /// <remarks>
    /// 等着的那条正在回答任务通知开出的一轮时，这里等到那一轮结束：两轮不会同时进行。取消（停下、宿主停机）时抛
    /// <see cref="OperationCanceledException"/>，等着的那条照常等下去。
    /// </remarks>
    internal async Task<CliLiveSession?> TakeAsync(Guid threadId, Guid successorRunId, CancellationToken cancellationToken)
    {
        if (!_waiting.TryGetValue(threadId, out var waiting) || !waiting.Request(successorRunId))
        {
            return null;
        }


        await using var registration = cancellationToken.Register(() => waiting.Delivered.TrySetCanceled(cancellationToken));
        return await waiting.Delivered.Task;
    }

    /// <summary>一条运行开始执行时占了一个并发槽：记在它名下，跟着它的进程走。</summary>
    public void HoldSlot(Guid runId, SemaphoreSlim slots) => _slots[runId] = Check.NotNull(slots);

    /// <summary>一条运行的执行结束：它名下还有槽就归还（交出了进程的运行名下已经没有）。</summary>
    public void ReleaseSlot(Guid runId)
    {
        SemaphoreSlim? slots;
        lock (_slotGate)
        {
            _slots.TryRemove(runId, out slots);
        }

        slots?.Release();
    }

    private void MoveSlot(Guid from, Guid to)
    {
        lock (_slotGate)
        {
            if (_slots.TryRemove(from, out var slots))
            {
                _slots[to] = slots;
            }
        }
    }
}

/// <summary>一条在等后台任务、可以交出进程的运行。</summary>
/// <param name="RunId">等着的运行。</param>
/// <param name="ThreadId">它的线程。</param>
/// <param name="AgentId">它的 Agent：只有同一 Agent 的下一条运行能接过进程。</param>
/// <param name="CliRuntimeId">它的运行时：只有落在同一运行时上的下一条运行能接过进程。</param>
public sealed record CliWaitingRun(Guid RunId, Guid ThreadId, Guid AgentId, Guid CliRuntimeId)
{
    private int _requested;

    /// <summary>来取进程的那条运行。</summary>
    public Guid? SuccessorRunId { get; private set; }

    /// <summary>有运行来取了（等着的运行据此在两轮之间停下）。</summary>
    internal TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>交出的进程；null = 没交出（自己另起进程）；取消 = 来取的那条放弃了。</summary>
    internal TaskCompletionSource<CliLiveSession?> Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>来取的那条放弃了：等着的运行重新登记，等下一个来取的。</summary>
    internal bool Abandoned => Delivered.Task.IsCanceled;

    /// <summary>只认第一个来取的。</summary>
    internal bool Request(Guid successorRunId)
    {
        if (Interlocked.Exchange(ref _requested, 1) == 1)
        {
            return false;
        }

        SuccessorRunId = successorRunId;
        Requested.TrySetResult();
        return true;
    }
}
