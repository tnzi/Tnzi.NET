namespace Tnzi.AI.Options;

/// <summary>
/// 执行效率监控配置 — 追踪工具循环中的 token 增量，检测 diminishing returns
/// </summary>
public class ExecutionBudgetOptions
{
    /// <summary>
    /// 是否启用执行效率监控。默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 低增量阈值（token 数）。单次工具循环 token 增量低于此值视为低效。默认 500。
    /// </summary>
    public int LowIncrementThreshold { get; set; } = 500;

    /// <summary>
    /// 连续低增量次数达到此值后注入 wrap up 提示。默认 3。
    /// </summary>
    public int ConsecutiveLowIncrementLimit { get; set; } = 3;

    /// <summary>
    /// Wrap up 提示消息
    /// </summary>
    public string WrapUpNudge { get; set; } =
        "You have been running tools with minimal progress for several iterations. Please wrap up your current work, summarize what you have accomplished, and present your findings.";
}
