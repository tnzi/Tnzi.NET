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
    /// 是否由本模块自带的后台循环驱动扫描（默认 true）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>此前没有任何调度者</b>：<c>ScanAsync</c> 在全仓零调用方，模块只把
    /// <c>WorkflowWatchdogService</c> 注册成 Scoped 服务，文档把"由宿主接调度"写成一个取舍，
    /// 却既没有接线示例也没有启动提示。结果是崩溃中断的执行实例永远停在 <c>Running</c>，
    /// <c>AwaitingApproval</c> 永不过期 —— 而 <c>Enabled</c> 默认 true，看上去它一直在工作。
    /// </para>
    /// <para>
    /// 要改用 Hangfire 之类的外部调度器时把这个关掉（而不是关 <see cref="Enabled"/>）：
    /// <see cref="Enabled"/>=false 是"根本不做超时检测"，两件事不能共用一个开关。
    /// </para>
    /// <para>
    /// 刻意不是 <c>[RuntimeSetting]</c>：它决定的是一个后台服务在启动时要不要跑，
    /// 放进设置中心会让人以为改完立刻生效。
    /// </para>
    /// </remarks>
    public bool UseBuiltInScheduler { get; set; } = true;

    /// <summary>
    /// 自带后台循环的扫描间隔（默认 5 分钟）。<see cref="UseBuiltInScheduler"/>=false 时无效。
    /// </summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 每次扫描最大处理数量（防止单次扫描积压过多）。默认 50。
    /// </summary>
    [RuntimeSetting(Label = "Max Batch Size", I18n = "admin.modules.system.settings.fields.workflowWatchdogMaxBatchSize",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Maximum number of stale executions processed per scan")]
    public int MaxBatchSize { get; set; } = 50;
}
