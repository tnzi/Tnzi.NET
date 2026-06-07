namespace Tnzi.AI.Workflow.Options;

/// <summary>
/// 工作流执行 Watchdog 配置项 — 控制超时检测行为
/// </summary>
public class WorkflowWatchdogOptions
{
    /// <summary>
    /// 是否启用 Watchdog（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Running 状态超时阈值（超过此时间未更新的执行实例视为超时）。默认 30 分钟。
    /// </summary>
    public TimeSpan RunningTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// AwaitingApproval / AwaitingInput 状态超时阈值（等待人工介入的执行实例）。默认 7 天。
    /// </summary>
    public TimeSpan WaitingTimeout { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 每次扫描最大处理数量（防止单次扫描积压过多）。默认 50。
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;
}
