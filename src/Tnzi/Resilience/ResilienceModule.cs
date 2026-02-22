
namespace Tnzi.Resilience;

/// <summary>
/// Resilience 模块
/// 提供重试、熔断、超时等弹性策略
/// </summary>
[DependsOn(typeof(CachingModule))]
public class ResilienceModule : TnziInfrastructureModule
{
    public override int LoadOrder => 5;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<ResilienceOptions>()
            .Bind(context.Configuration.GetSection("Resilience"))
            .ValidateWith<ResilienceOptions, ResilienceOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var options = new ResilienceOptions();
        context.Configuration.GetSection("Resilience").Bind(options);

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // 添加 Resilience Pipeline
        context.Services.AddResiliencePipeline("default", builder =>
        {
            // 重试策略
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(options.RetryDelayBaseMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

            // 熔断策略
            builder.AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
                FailureRatio = options.CircuitBreakerFailureRatio,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds)
            });
        });

        // 添加事件总线专用 Pipeline
        context.Services.AddResiliencePipeline("eventbus", builder =>
        {
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Exponential
            });
        });

        return Task.CompletedTask;
    }
}
