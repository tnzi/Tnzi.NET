using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Tnzi.AI.Middleware;

/// <summary>
/// 重试中间件 — 对瞬态 LLM API 失败执行指数退避重试 + 熔断保护
/// </summary>
/// <remarks>
/// 重试条件：HTTP 429 (Rate Limit)、5xx 服务端错误、TaskCanceledException（连接超时）。
/// 不重试：4xx（除 429）、BusinessException、GuardrailRejectedException 等业务异常。
/// 流式路径：仅重试连接阶段（第一个 chunk 获取前），不重试已开始的流。
/// </remarks>
public class RetryMiddleware : IAiMiddleware
{
    private readonly IOptions<AIOptions> _options;
    private readonly ILogger<RetryMiddleware> _logger;
    private readonly ResiliencePipeline _pipeline;

    public int Order => AiMiddlewareOrders.Retry;

    public RetryMiddleware(IOptions<AIOptions> options, ILogger<RetryMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);

        var retryOptions = options.Value.Retry;
        _pipeline = BuildPipeline(retryOptions);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Retry.Enabled)
            return await next(context, cancellationToken);

        if (context.Agent.ExecutionMode == AgentExecutionMode.ExternalCli)
            return await next(context, cancellationToken);

        return await _pipeline.ExecuteAsync(async ct => await next(context, ct), cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Retry.Enabled || context.Agent.ExecutionMode == AgentExecutionMode.ExternalCli)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        // 流式重试：仅重试连接阶段（获取第一个 chunk 前的异常）
        // 一旦流开始输出就不再重试，避免重复输出。
        // 使用 ConnectWithRetryAsync 获取已连接的流（第一个 chunk 已缓冲），
        // 然后在无 catch 的环境中 yield。
        var (firstChunk, connectedStream) = await ConnectWithRetryAsync(context, next, cancellationToken);

        if (firstChunk == null || connectedStream == null)
            yield break;

        yield return firstChunk;

        await foreach (var chunk in connectedStream.WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 带重试的流式连接 — 重试获取第一个 chunk，成功后返回缓冲的首个 chunk 和剩余流
    /// </summary>
    private async Task<(AgentStreamChunk? FirstChunk, IAsyncEnumerable<AgentStreamChunk>? RemainingStream)> ConnectWithRetryAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, CancellationToken cancellationToken)
    {
        var retryOptions = _options.Value.Retry;
        var maxRetries = retryOptions.MaxRetries;
        var attempt = 0;
        var delay = retryOptions.InitialDelay;

        while (true)
        {
            try
            {
                var enumerable = next(context, cancellationToken);
                var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

                var hasFirst = await enumerator.MoveNextAsync();
                if (!hasFirst)
                {
                    await enumerator.DisposeAsync();
                    return (null, null);
                }

                var firstChunk = enumerator.Current;
                // 包装剩余流（负责最终 dispose）
                var remaining = ConsumeRemainingAsync(enumerator, cancellationToken);
                return (firstChunk, remaining);
            }
            catch (Exception ex) when (attempt < maxRetries && ShouldRetry(ex))
            {
                attempt++;
                _logger.LogWarning(ex,
                    "Streaming connection failed (attempt {Attempt}/{MaxRetries}), retrying after {Delay}ms",
                    attempt, maxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * retryOptions.BackoffMultiplier, retryOptions.MaxDelay.TotalMilliseconds));
            }
        }
    }

    /// <summary>
    /// 消费剩余流并确保 enumerator 被 dispose
    /// </summary>
    private static async IAsyncEnumerable<AgentStreamChunk> ConsumeRemainingAsync(
        IAsyncEnumerator<AgentStreamChunk> enumerator,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    /// <summary>
    /// 构建 Polly ResiliencePipeline（重试 + 熔断）
    /// </summary>
    private ResiliencePipeline BuildPipeline(RetryOptions retryOptions)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retryOptions.MaxRetries,
                Delay = retryOptions.InitialDelay,
                MaxDelay = retryOptions.MaxDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ShouldRetry),
                OnRetry = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "AI request failed (attempt {Attempt}/{MaxRetries}), retrying after {Delay}ms",
                        args.AttemptNumber + 1, retryOptions.MaxRetries,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(Math.Max(
                    retryOptions.CircuitBreakerDuration.TotalSeconds * 2, 30)),
                MinimumThroughput = retryOptions.CircuitBreakerFailureThreshold,
                BreakDuration = retryOptions.CircuitBreakerDuration,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ShouldRetry)
            })
            .Build();
    }

    /// <summary>
    /// 判断异常是否应触发重试
    /// </summary>
    private static bool ShouldRetry(Exception ex)
    {
        // 业务异常不重试
        if (ex is BusinessException)
            return false;

        // 取消操作不重试（用户主动取消）
        if (ex is OperationCanceledException)
            return false;

        // TaskCanceledException（连接超时）应重试
        if (ex is TaskCanceledException tce && tce.InnerException is TimeoutException)
            return true;

        // HTTP 异常：429 和 5xx 重试，其他 4xx 不重试
        if (ex is HttpRequestException httpEx)
        {
            var statusCode = (int?)httpEx.StatusCode;
            if (statusCode == 429) return true;
            if (statusCode >= 500) return true;
            return false;
        }

        // 未知异常默认重试（网络中断等）
        return true;
    }
}
