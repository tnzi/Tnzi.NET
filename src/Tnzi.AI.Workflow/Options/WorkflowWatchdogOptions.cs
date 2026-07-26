namespace Tnzi.AI.Workflow.Options;

/// <summary>
/// 工作流执行 Watchdog 配置项 - 控制超时检测行为
/// </summary>
[ConfigSection("AI:WorkflowWatchdog")]
[RuntimeSettingGroup(Key = "ai-workflow", Module = "AI", DisplayName = "Workflow Watchdog",
    I18nKey = "admin.modules.system.settings.groups.aiWorkflow", Icon = "mdi:timer-alert-outline", Order = 156)]
public class WorkflowWatchdogOptions
{
    /// <summary>
    /// 是否启用 Watchdog（默认 true）
    /// </summary>
    [RuntimeSetting(Label = "Watchdog Enabled", I18n = "admin.modules.system.settings.fields.workflowWatchdogEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Enable scanning for stale workflow executions and marking them as timed out")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Running 状态超时阈值（超过此时间未更新的执行实例视为超时）。默认 30 分钟。
    /// </summary>
    [RuntimeSetting(Label = "Running Timeout", I18n = "admin.modules.system.settings.fields.workflowWatchdogRunningTimeout",
        Type = SettingFieldType.Duration,
        Description = "Executions stuck in Running beyond this duration are marked as timed out")]
    public TimeSpan RunningTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// AwaitingApproval / AwaitingInput 状态超时阈值（等待人工介入的执行实例）。默认 7 天。
    /// </summary>
    [RuntimeSetting(Label = "Waiting Timeout", I18n = "admin.modules.system.settings.fields.workflowWatchdogWaitingTimeout",
        Type = SettingFieldType.Duration,
        Description = "Executions awaiting approval/input beyond this duration are marked as timed out")]
    public TimeSpan WaitingTimeout { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 每次扫描最大处理数量（防止单次扫描积压过多）。默认 50。
    /// </summary>
    [RuntimeSetting(Label = "Max Batch Size", I18n = "admin.modules.system.settings.fields.workflowWatchdogMaxBatchSize",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Maximum number of stale executions processed per scan")]
    public int MaxBatchSize { get; set; } = 50;
}
