namespace Tnzi.AI.Options;

/// <summary>
/// AI 重试与熔断配置选项
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 是否启用重试中间件（默认启用）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大重试次数（默认 3）
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 初始重试延迟（默认 1 秒）
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大重试延迟（默认 30 秒）
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 退避乘数（默认 2.0，指数退避）
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// 熔断器失败阈值（连续失败多少次后触发熔断，默认 5）
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// 熔断器开启持续时间（默认 1 分钟）
    /// </summary>
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 429/529 响应中 Retry-After 的最大等待时间（秒）。超过则进入 cooldown 而非重试。默认 20。
    /// </summary>
    public int MaxRetryAfterSeconds { get; set; } = 20;

    /// <summary>
    /// 后台/辅助任务遇到 429 时是否立即放弃（防雪崩）。默认 true。
    /// </summary>
    public bool AbortBackgroundOn429 { get; set; } = true;
}
