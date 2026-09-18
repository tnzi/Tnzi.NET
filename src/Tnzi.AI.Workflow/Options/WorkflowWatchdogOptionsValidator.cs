namespace Tnzi.AI.Workflow.Options;

/// <summary>
/// 工作流 Watchdog 配置验证器
/// </summary>
public class WorkflowWatchdogOptionsValidator : OptionsValidatorBase<WorkflowWatchdogOptions>
{
    protected override void ValidateOptions(WorkflowWatchdogOptions options, List<string> errors)
    {
        Check.NotNull(options);

        if (options.RunningTimeout <= TimeSpan.Zero)
        {
            AddError(errors, nameof(options.RunningTimeout),
                $"RunningTimeout must be a positive duration, got {options.RunningTimeout}.");
        }

        if (options.HeartbeatInterval < TimeSpan.FromSeconds(1))
        {
            AddError(errors, nameof(options.HeartbeatInterval),
                $"HeartbeatInterval must be at least 1 second, got {options.HeartbeatInterval}.");
        }
        else if (options.RunningTimeout > TimeSpan.Zero && options.HeartbeatInterval > options.RunningTimeout / 2)
        {
            // 心跳比超时阈值的一半还稀，一次错过的心跳就足以让一个活着的执行被回收。
            // 错误记在 RunningTimeout 名下：这对里只有它是 [RuntimeSetting]，设置中心把它调低时
            // 校验在这里失败，报的字段必须是页面上那个，说的下限必须是它能改成的值。
            AddError(errors, nameof(options.RunningTimeout),
                $"RunningTimeout ({options.RunningTimeout}) must be at least 2 x HeartbeatInterval ({options.HeartbeatInterval}), i.e. {options.HeartbeatInterval * 2}; a live execution must always look fresher than the cutoff.");
        }

        if (options.WaitingTimeout <= TimeSpan.Zero)
        {
            AddError(errors, nameof(options.WaitingTimeout),
                $"WaitingTimeout must be a positive duration, got {options.WaitingTimeout}.");
        }

        // 下限 10 秒：比这更密的扫描只会把数据库当成轮询目标，而超时判定本身以分钟计。
        if (options.UseBuiltInScheduler && options.ScanInterval < TimeSpan.FromSeconds(10))
        {
            AddError(errors, nameof(options.ScanInterval),
                $"ScanInterval must be at least 10 seconds, got {options.ScanInterval}.");
        }

        if (options.MaxBatchSize < 1)
        {
            AddError(errors, nameof(options.MaxBatchSize),
                $"MaxBatchSize must be at least 1, got {options.MaxBatchSize}.");
        }
    }
}
