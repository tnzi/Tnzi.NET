namespace Tnzi.AI.Options;

/// <summary>
/// AI 重试与熔断配置选项
/// </summary>
/// <remarks>
/// 仅 <see cref="Enabled"/> 作为可热配字段暴露：它在每次调用（流式/非流式）都被重新读取并有效门控。
/// 其余调优字段（MaxRetries / 延迟 / 退避 / 熔断阈值 / MaxRetryAfterSeconds）不暴露 ——
/// <c>RetryMiddleware</c> 是单例，其 Polly 管线（含带状态的熔断器，状态须跨请求累积）在构造期一次性构建，
/// 非流式路径复用该缓存管线、熔断字段永不重读，故这些字段对主路径热更新无效。
/// 若要热配调优参数，需给 RetryMiddleware 增加 <c>IOptionsMonitor.OnChange</c> 管线重建（消费方改动，另行评估）。
/// </remarks>
[ConfigSection("AI:Retry")]
[RuntimeSettingGroup(Key = "ai-retry", Module = "AI", DisplayName = "Retry & Resilience",
    I18nKey = "admin.modules.system.settings.groups.aiRetry", Icon = "mdi:restart", Order = 170)]
public class RetryOptions
{
    /// <summary>
    /// 是否启用重试中间件（默认启用）
    /// </summary>
    [RuntimeSetting(Label = "Retry Enabled", I18n = "admin.modules.system.settings.fields.retryEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Enable automatic retry + circuit breaker for AI API calls")]
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
