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

        if (options.MaxBatchSize < 1)
        {
            AddError(errors, nameof(options.MaxBatchSize),
                $"MaxBatchSize must be at least 1, got {options.MaxBatchSize}.");
        }
    }
}
