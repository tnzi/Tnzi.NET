
namespace Tnzi.Performance.Options;

/// <summary>
/// 性能分析配置选项验证器
/// </summary>
public class PerformanceOptionsValidator : OptionsValidatorBase<PerformanceOptions>
{
    /// <summary>
    /// 验证性能分析配置选项
    /// </summary>
    protected override void ValidateOptions(PerformanceOptions options, List<string> errors)
    {
        if (!options.Enabled)
        {
            // 未启用时不需要验证其他配置
            return;
        }

        if (options.MaxHistorySize <= 0)
        {
            errors.Add("Performance.MaxHistorySize must be greater than 0.");
        }

        if (options.SlowRequestThresholdMs.HasValue && options.SlowRequestThresholdMs.Value <= 0)
        {
            errors.Add("Performance.SlowRequestThresholdMs must be greater than 0 when set.");
        }
    }
}
