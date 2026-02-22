namespace Tnzi.Resilience;

/// <summary>
/// Resilience 配置选项
/// </summary>
public class ResilienceOptions
{
    /// <summary>
    /// 是否启用 Resilience 功能
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// 重试延迟基数（毫秒）
    /// 实际延迟 = 基数 * 2^(重试次数-1)
    /// </summary>
    public int RetryDelayBaseMs { get; set; } = 200;

    /// <summary>
    /// 熔断器采样持续时间（秒）
    /// </summary>
    public int CircuitBreakerSamplingDurationSeconds { get; set; } = 30;

    /// <summary>
    /// 熔断器失败率阈值（0.0 - 1.0）
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// 熔断器最小吞吐量
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// 熔断器打开持续时间（秒）
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;
}
