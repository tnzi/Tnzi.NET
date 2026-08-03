namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 分层看门狗：空闲 / 工具 / 硬超时。
/// </summary>
/// <remarks>
/// <para>
/// <b>默认不设硬超时。</b>一个持续产出事件的长任务不该仅仅因为跑得久被杀 ——
/// 外部 agent 适合的正是那种「一次派一个完整任务」的用法，几小时是正常量级。
/// 真正需要判死的是<b>没有进展</b>：完全没有事件（空闲），或有工具调用却迟迟收不到结果。
/// </para>
/// <para>
/// 分层的意义在于两种停滞的合理等待时长差一个数量级：一次 <c>npm install</c> 跑十分钟
/// 很正常，而十分钟一个事件都不发几乎一定是卡死了。用同一个阈值必然要么误杀要么放过。
/// </para>
/// </remarks>
public sealed class CliRunWatchdog : IDisposable
{
    private readonly TimeSpan _idle;
    private readonly TimeSpan _tool;
    private readonly TimeSpan? _hard;
    private readonly CancellationTokenSource _cts;
    private readonly DateTime _startedAt;
    private readonly Lock _gate = new();

    private DateTime _lastEventAt;
    private DateTime? _pendingToolSince;
    private int _pendingToolCount;
    private CliRunFailureReason? _failure;

    /// <summary>初始化看门狗并开始计时。</summary>
    /// <param name="idle">空闲阈值。</param>
    /// <param name="tool">工具阈值。</param>
    /// <param name="hard">硬超时；null = 关闭。</param>
    /// <param name="linkedTo">外部取消令牌（用户取消 / 宿主停机）。</param>
    public CliRunWatchdog(TimeSpan idle, TimeSpan tool, TimeSpan? hard, CancellationToken linkedTo)
    {
        _idle = idle;
        _tool = tool;
        _hard = hard;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(linkedTo);
        _startedAt = DateTime.UtcNow;
        _lastEventAt = _startedAt;
    }

    /// <summary>供执行循环使用的令牌。看门狗触发时被取消。</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>看门狗判定的失败类型；未触发则为 null。</summary>
    public CliRunFailureReason? Failure
    {
        get
        {
            lock (_gate)
            {
                return _failure;
            }
        }
    }

    /// <summary>记录一个事件，重置空闲计时并维护工具配对状态。</summary>
    public void Observe(CliAgentEvent evt)
    {
        lock (_gate)
        {
            _lastEventAt = DateTime.UtcNow;

            switch (evt.Type)
            {
                case CliAgentEventType.ToolUse:
                    _pendingToolCount++;
                    _pendingToolSince ??= _lastEventAt;
                    break;

                case CliAgentEventType.ToolResult:
                    _pendingToolCount = Math.Max(0, _pendingToolCount - 1);
                    // 还有别的工具在跑就重新起算，而不是清零 —— 并行工具调用下
                    // 清零会让最慢的那个工具永远等不到判决。
                    _pendingToolSince = _pendingToolCount > 0 ? _lastEventAt : null;
                    break;
            }
        }
    }

    /// <summary>
    /// 检查是否应当判死。由执行循环定期调用。
    /// </summary>
    /// <returns>触发时返回 true 并已请求取消。</returns>
    public bool CheckAndTrip()
    {
        lock (_gate)
        {
            if (_failure is not null)
            {
                return true;
            }

            var now = DateTime.UtcNow;

            if (_hard is { } hard && now - _startedAt > hard)
            {
                return Trip(CliRunFailureReason.HardTimeout);
            }

            // 有工具在跑时用工具阈值，此时"没有事件"是合理的（工具正在干活）。
            if (_pendingToolSince is { } toolSince)
            {
                return now - toolSince > _tool && Trip(CliRunFailureReason.ToolTimeout);
            }

            return now - _lastEventAt > _idle && Trip(CliRunFailureReason.IdleTimeout);
        }
    }

    private bool Trip(CliRunFailureReason reason)
    {
        _failure = reason;
        _cts.Cancel();
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();
}
