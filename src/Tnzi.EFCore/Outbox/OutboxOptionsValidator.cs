
namespace Tnzi.EFCore.Outbox;

/// <summary>
/// Outbox 配置选项验证器
/// </summary>
public class OutboxOptionsValidator : OptionsValidatorBase<OutboxOptions>
{
    protected override void ValidateOptions(OutboxOptions options, List<string> errors)
    {
        if (options.PollingIntervalSeconds < 1)
            AddError(errors, nameof(options.PollingIntervalSeconds), "must be at least 1 second.", 1);

        if (options.MaxRetryCount < 1)
            AddError(errors, nameof(options.MaxRetryCount), "must be at least 1.", 1);

        if (options.BatchSize < 1)
            AddError(errors, nameof(options.BatchSize), "must be at least 1.", 1);

        if (options.RetentionDays < 1)
            AddError(errors, nameof(options.RetentionDays), "must be at least 1 day.", 1);
    }
}
