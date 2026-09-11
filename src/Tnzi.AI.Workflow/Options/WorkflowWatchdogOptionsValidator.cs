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
