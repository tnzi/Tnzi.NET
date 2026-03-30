namespace Tnzi.Audit.Options;

/// <summary>
/// 审计模块配置选项验证器
/// </summary>
public class AuditOptionsValidator : OptionsValidatorBase<AuditOptions>
{
    protected override void ValidateOptions(AuditOptions options, List<string> errors)
    {
        if (options.RetentionDays <= 0)
        {
            AddError(errors, nameof(options.RetentionDays), "RetentionDays must be greater than 0.");
        }

        if (options.BatchSize is <= 0 or > 1000)
        {
            AddError(errors, nameof(options.BatchSize), "BatchSize must be between 1 and 1000.");
        }

        if (options.ChannelCapacity < 0)
        {
            AddError(errors, nameof(options.ChannelCapacity), "ChannelCapacity must be 0 (unbounded) or greater.");
        }

        if (options.MaxRequestBodySize is <= 0 or > 65536)
        {
            AddError(errors, nameof(options.MaxRequestBodySize), "MaxRequestBodySize must be between 1 and 65536.");
        }
    }
}
