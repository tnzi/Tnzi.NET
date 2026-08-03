
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
        context.Services.AddTnziOptions<ResilienceOptions, ResilienceOptionsValidator>(context.Configuration);

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

        // 通用管线：框架自身不消费，供消费应用调外部服务时用（键见 ResiliencePipelineNames）
        context.Services.AddResiliencePipeline(ResiliencePipelineNames.Default, builder =>
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
        // 重试参数与 EventBusOptions 对齐(单一参数来源):LocalEventBus 在 EnableRetry 时优先消费此管线
        context.Services.AddResiliencePipeline(ResiliencePipelineNames.EventBus, (builder, pipelineContext) =>
        {
            var eventBusOptions = pipelineContext.ServiceProvider.GetService<IOptions<EventBusOptions>>()?.Value;
            var maxRetries = eventBusOptions?.RetryCount ?? 3;
            var baseDelayMs = eventBusOptions?.RetryIntervalMs ?? 1000;

            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries > 0 ? maxRetries : 3,
                Delay = TimeSpan.FromMilliseconds(baseDelayMs > 0 ? baseDelayMs : 1000),
                BackoffType = DelayBackoffType.Exponential
            });
        });

        return Task.CompletedTask;
    }
}
