namespace Tnzi.Resilience;

/// <summary>
/// <see cref="ResilienceModule"/> 注册的命名弹性管线的键。
/// </summary>
/// <remarks>
/// 用法（管线未注册时优雅降级 —— <c>Resilience:Enabled=false</c> 时它们都不存在）：
/// <code>
/// var provider = serviceProvider.GetService&lt;ResiliencePipelineProvider&lt;string&gt;&gt;();
/// if (provider != null &amp;&amp; provider.TryGetPipeline(ResiliencePipelineNames.Default, out var pipeline))
///     await pipeline.ExecuteAsync(async ct =&gt; await CallRemoteAsync(ct), cancellationToken);
/// </code>
///
/// ★<see cref="Default"/> 是**给消费应用用的通用管线**（重试 + 熔断，参数全部来自
/// <c>Resilience</c> 配置节），框架自身不消费它 —— 框架内的重试都带各自的失败判据与预算
/// （LLM 重问、MCP 重连、Kafka 消费者重连、乐观并发冲突重试），套同一条通用管线是错的。
/// 唯一由框架消费的是 <see cref="EventBus"/>。
/// </remarks>
public static class ResiliencePipelineNames
{
    /// <summary>
    /// 通用管线：指数退避重试（带抖动）+ 熔断。供消费应用调用外部服务时使用。
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// 事件总线专用管线：仅重试，参数与 <c>EventBusOptions</c> 对齐（单一参数来源）。
    /// 由 <c>LocalEventBus</c> 在 <c>EnableRetry</c> 时消费。
    /// </summary>
    public const string EventBus = "eventbus";
}
