
namespace Tnzi.Resilience;

/// <summary>
/// Resilience 配置选项验证器
/// </summary>
public class ResilienceOptionsValidator : OptionsValidatorBase<ResilienceOptions>
{
    /// <summary>
    /// 验证 Resilience 配置选项
    /// </summary>
    protected override void ValidateOptions(ResilienceOptions options, List<string> errors)
    {
        if (!options.Enabled)
        {
            // 如果未启用，不需要验证其他配置
            return;
        }

        // 验证最大重试次数
        if (options.MaxRetryAttempts <= 0)
        {
            errors.Add("Resilience.MaxRetryAttempts must be greater than 0.");
        }

        // 验证重试延迟基数
        if (options.RetryDelayBaseMs <= 0)
        {
            errors.Add("Resilience.RetryDelayBaseMs must be greater than 0.");
        }

        // 验证熔断器采样持续时间
        if (options.CircuitBreakerSamplingDurationSeconds <= 0)
        {
            errors.Add("Resilience.CircuitBreakerSamplingDurationSeconds must be greater than 0.");
        }

        // 验证熔断器失败率阈值
        if (options.CircuitBreakerFailureRatio < 0.0 || options.CircuitBreakerFailureRatio > 1.0)
        {
            errors.Add("Resilience.CircuitBreakerFailureRatio must be between 0.0 and 1.0.");
        }

        // 验证熔断器最小吞吐量
        if (options.CircuitBreakerMinimumThroughput <= 0)
        {
            errors.Add("Resilience.CircuitBreakerMinimumThroughput must be greater than 0.");
        }

        // 验证熔断器打开持续时间
        if (options.CircuitBreakerBreakDurationSeconds <= 0)
        {
            errors.Add("Resilience.CircuitBreakerBreakDurationSeconds must be greater than 0.");
        }
    }
}
