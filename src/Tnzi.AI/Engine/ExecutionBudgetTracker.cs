namespace Tnzi.AI.Engine;

/// <summary>
/// 执行效率追踪器 — 在 AgentExecutor 工具循环内追踪 token 增量，
/// 检测 diminishing returns（连续低效迭代），触发 wrap up 提示注入。
/// </summary>
/// <remarks>
/// 借鉴 Claude Code 的 diminishing returns 检测：
/// 连续 N 次工具循环的 token 增量低于阈值时，注入 "wrap up" nudge 消息。
/// 仅在主 Agent 执行时使用，子 Agent 不受限。
/// </remarks>
public class ExecutionBudgetTracker
{
    private readonly int _lowIncrementThreshold;
    private readonly int _consecutiveLowIncrementLimit;
    private readonly string _wrapUpNudge;
    private int _consecutiveLowIncrements;
    private int _lastTotalTokens;
    private bool _nudgeInjected;

    public ExecutionBudgetTracker(ExecutionBudgetOptions options)
    {
        Check.NotNull(options);
        _lowIncrementThreshold = Math.Max(1, options.LowIncrementThreshold);
        _consecutiveLowIncrementLimit = Math.Max(1, options.ConsecutiveLowIncrementLimit);
        _wrapUpNudge = Check.NotNullOrWhiteSpace(options.WrapUpNudge);
    }

    /// <summary>
    /// 记录一次工具循环后的 token 总量，检测是否应注入 wrap up 提示
    /// </summary>
    /// <param name="totalOutputTokens">当前累计输出 token 数</param>
    /// <returns>需要注入的 nudge 消息，如果不需要则返回 null</returns>
    public string? RecordIteration(int totalOutputTokens)
    {
        if (_nudgeInjected)
            return null; // 只注入一次

        var increment = totalOutputTokens - _lastTotalTokens;
        _lastTotalTokens = totalOutputTokens;

        if (increment < _lowIncrementThreshold)
        {
            _consecutiveLowIncrements++;
        }
        else
        {
            _consecutiveLowIncrements = 0; // 重置计数
        }

        if (_consecutiveLowIncrements >= _consecutiveLowIncrementLimit)
        {
            _nudgeInjected = true;
            return _wrapUpNudge;
        }

        return null;
    }

    /// <summary>
    /// 当前连续低增量次数
    /// </summary>
    public int ConsecutiveLowIncrements => _consecutiveLowIncrements;

    /// <summary>
    /// 是否已注入 wrap up 提示
    /// </summary>
    public bool NudgeInjected => _nudgeInjected;
}
