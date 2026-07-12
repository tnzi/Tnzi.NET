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
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<RetryMiddleware> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly ResiliencePipeline _backgroundPipeline;

    public int Order => AiMiddlewareOrders.Retry;

    public RetryMiddleware(IOptionsMonitor<AIOptions> options, ILogger<RetryMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);

        // Polly pipeline (含带状态的熔断器) 在构造期按当前配置构建一次并复用——
        // 熔断器状态跨请求累积，逐请求重建会丢失状态，故此处不做热更新（见转换报告）。
        var retryOptions = options.CurrentValue.Retry;
        _pipeline = BuildPipeline(retryOptions, excludeRateLimitRetry: false);
        _backgroundPipeline = BuildPipeline(retryOptions, excludeRateLimitRetry: retryOptions.AbortBackgroundOn429);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Retry.Enabled)
            return await next(context, cancellationToken);


        var pipeline = IsBackgroundTask(context) ? _backgroundPipeline : _pipeline;
        return await pipeline.ExecuteAsync(async ct => await next(context, ct), cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Retry.Enabled)
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
        var retryOptions = _options.CurrentValue.Retry;
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
            catch (Exception ex) when (attempt < maxRetries && ShouldRetry(ex) && !ShouldAbortBackground(context, ex))
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
    /// <param name="retryOptions">重试配置</param>
    /// <param name="excludeRateLimitRetry">是否排除 429 Rate Limit 的重试（后台任务防雪崩）</param>
    private ResiliencePipeline BuildPipeline(RetryOptions retryOptions, bool excludeRateLimitRetry)
    {
        var predicate = CreateShouldRetryPredicate(excludeRateLimitRetry);
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retryOptions.MaxRetries,
                Delay = retryOptions.InitialDelay,
                MaxDelay = retryOptions.MaxDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(predicate),
                OnRetry = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "AI request failed (attempt {Attempt}/{MaxRetries}), retrying after {Delay}ms",
                        args.AttemptNumber + 1, retryOptions.MaxRetries,
                        args.RetryDelay.TotalMilliseconds);

                    // 检查 Retry-After 是否超过最大等待阈值
                    var retryAfterSeconds = ParseRetryAfterSeconds(args.Outcome.Exception);
                    if (retryAfterSeconds > retryOptions.MaxRetryAfterSeconds)
                    {
                        _logger.LogWarning(
                            "Retry-After header value ({RetryAfterSeconds}s) exceeds MaxRetryAfterSeconds ({MaxRetryAfterSeconds}s), entering cooldown",
                            retryAfterSeconds, retryOptions.MaxRetryAfterSeconds);
                    }

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
                ShouldHandle = new PredicateBuilder().Handle<Exception>(predicate)
            })
            .Build();
    }

    /// <summary>
    /// 构建共享的异常过滤谓词（Retry + CircuitBreaker 复用）
    /// </summary>
    private static Func<Exception, bool> CreateShouldRetryPredicate(bool excludeRateLimitRetry)
    {
        return ex => ShouldRetry(ex) && !(excludeRateLimitRetry && Is429RateLimit(ex));
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

        // HTTP 异常：429 和 5xx 重试；无状态码（连接层瞬时失败，如 DNS/连接重置）重试；其他 4xx 不重试
        if (ex is HttpRequestException httpEx)
        {
            var statusCode = (int?)httpEx.StatusCode;
            if (statusCode is null) return true; // 网络层失败，无 HTTP 响应
            if (statusCode == 429) return true;
            if (statusCode >= 500) return true;
            return false;
        }

        // 明确的网络层瞬时异常重试（连接重置、套接字错误等）
        if (ex is System.IO.IOException || ex is System.Net.Sockets.SocketException)
            return true;

        // 其他未知异常 fail-fast，避免对非幂等工具调用重复执行产生副作用
        return false;
    }

    /// <summary>
    /// 判断异常是否为 429 Rate Limit
    /// </summary>
    private static bool Is429RateLimit(Exception ex)
    {
        return ex is HttpRequestException httpEx && (int?)httpEx.StatusCode == 429;
    }

    /// <summary>
    /// 判断当前上下文是否为后台/辅助任务
    /// </summary>
    private static bool IsBackgroundTask(AiMiddlewareContext context)
    {
        return context.Properties.TryGetValue("is_background_task", out var value) && value is true;
    }

    /// <summary>
    /// 后台任务遇到 429 时是否应中止重试（防雪崩）
    /// </summary>
    private bool ShouldAbortBackground(AiMiddlewareContext context, Exception ex)
    {
        return _options.CurrentValue.Retry.AbortBackgroundOn429
               && IsBackgroundTask(context)
               && ex is HttpRequestException httpEx
               && (int?)httpEx.StatusCode == 429;
    }

    /// <summary>
    /// 解析 Retry-After 头部值（秒）。支持从异常 Data 字典中获取。
    /// </summary>
    private static int? ParseRetryAfterSeconds(Exception? ex)
    {
        if (ex is HttpRequestException httpEx && httpEx.Data.Contains("Retry-After"))
        {
            var raw = httpEx.Data["Retry-After"]?.ToString();
            if (int.TryParse(raw, out var seconds))
                return seconds;
        }

        return null;
    }
}
