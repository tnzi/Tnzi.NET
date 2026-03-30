namespace Tnzi.AI.Middleware;

/// <summary>
/// 用量日志中间件 — After only: 记录 UsageLog + 发送 OTel 指标
/// </summary>
public class UsageLoggingMiddleware : IAiMiddleware
{
    private readonly IUsageLogService _usageLogService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UsageLoggingMiddleware> _logger;

    public int Order => AiMiddlewareOrders.UsageLogging;

    public UsageLoggingMiddleware(
        IUsageLogService usageLogService,
        IServiceScopeFactory scopeFactory,
        ILogger<UsageLoggingMiddleware> logger)
    {
        _usageLogService = Check.NotNull(usageLogService);
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var isSuccess = true;
        string? errorMessage = null;

        AgentRunResult result;
        try
        {
            result = await next(context, cancellationToken);
        }
        catch (Exception ex)
        {
            isSuccess = false;
            errorMessage = ex.Message;
            sw.Stop();

            // 记录失败日志（用 CancellationToken.None，原 token 可能已取消）
            await LogUsageSafeAsync(context, 0, 0, 0, 0, sw.ElapsedMilliseconds, false, ex.Message, CancellationToken.None);

            // 记录 OTel 错误指标
            AIActivitySource.RecordError(context.Agent.Provider, context.Agent.Model, ex.GetType().Name);

            throw;
        }

        sw.Stop();

        // After: 记录用量日志
        var inputTokens = result.Usage?.InputTokens ?? 0;
        var outputTokens = result.Usage?.OutputTokens ?? 0;
        var cachedInputTokens = result.Usage?.CachedInputTokens ?? 0;
        var cacheCreationTokens = result.Usage?.CacheCreationTokens ?? 0;

        await LogUsageSafeAsync(context, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens, sw.ElapsedMilliseconds, isSuccess, errorMessage, cancellationToken);

        // 记录 OTel 指标
        AIActivitySource.RecordChatRequest(context.Agent.Provider, context.Agent.Model);
        AIActivitySource.RecordTokenUsage(context.Agent.Provider, context.Agent.Model, inputTokens, outputTokens);
        AIActivitySource.RecordChatLatency(context.Agent.Provider, context.Agent.Model, sw.Elapsed.TotalSeconds);

        return result;
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        TokenUsageDto? lastUsage = null;
        var isSuccess = true;
        string? errorMessage = null;

        IAsyncEnumerable<AgentStreamChunk> stream;
        try
        {
            stream = next(context, cancellationToken);
        }
        catch (Exception ex)
        {
            isSuccess = false;
            errorMessage = ex.Message;
            sw.Stop();
            await LogUsageSafeAsync(context, 0, 0, 0, 0, sw.ElapsedMilliseconds, false, ex.Message, cancellationToken);
            AIActivitySource.RecordError(context.Agent.Provider, context.Agent.Model, ex.GetType().Name);
            throw;
        }

        var completedNormally = false;
        try
        {
            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                if (chunk.Usage != null)
                {
                    lastUsage = chunk.Usage;
                }
                yield return chunk;
            }
            completedNormally = true;
        }
        finally
        {
            sw.Stop();
            var inputTokens = lastUsage?.InputTokens ?? 0;
            var outputTokens = lastUsage?.OutputTokens ?? 0;
            if (!completedNormally)
            {
                isSuccess = false;
                errorMessage = "Streaming was cancelled or failed";
            }
            var cachedInputTokens = lastUsage?.CachedInputTokens ?? 0;
            var cacheCreationTokens = lastUsage?.CacheCreationTokens ?? 0;
            await LogUsageSafeAsync(context, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens, sw.ElapsedMilliseconds, isSuccess, errorMessage, CancellationToken.None);
            AIActivitySource.RecordChatRequest(context.Agent.Provider, context.Agent.Model, "chat_streaming");
            AIActivitySource.RecordTokenUsage(context.Agent.Provider, context.Agent.Model, inputTokens, outputTokens);
            AIActivitySource.RecordChatLatency(context.Agent.Provider, context.Agent.Model, sw.Elapsed.TotalSeconds);
        }
    }

    /// <summary>
    /// 安全记录用量日志。streaming 场景下请求 scope 可能已释放，
    /// 因此先尝试已注入的 service，失败后用独立 scope 重试。
    /// </summary>
    private async Task LogUsageSafeAsync(AiMiddlewareContext context, int inputTokens, int outputTokens, int cachedInputTokens, int cacheCreationTokens, long durationMs, bool isSuccess, string? errorMessage, CancellationToken ct)
    {
        var provider = context.Agent.Provider;
        var model = context.Agent.Model ?? "unknown";
        var agentId = context.Agent.AgentId;
        var threadId = context.Request.ThreadId;

        _logger.LogInformation(
            "AI usage: provider={Provider} model={Model} input={Input} output={Output} cached={Cached} creation={Creation} duration={Duration}ms success={Success}",
            provider, model, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens, durationMs, isSuccess);

        try
        {
            await _usageLogService.LogUsageAsync(
                operationType: AIOperationType.Chat, provider: provider, model: model,
                inputTokens: inputTokens, outputTokens: outputTokens, durationMs: durationMs,
                isSuccess: isSuccess, errorMessage: errorMessage, agentId: agentId, threadId: threadId,
                cachedInputTokens: cachedInputTokens, cacheCreationTokens: cacheCreationTokens, ct: ct);
        }
        catch (ObjectDisposedException)
        {
            // Request scope already disposed (streaming scenario) — retry with independent scope
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<IUsageLogService>();
                await scopedService.LogUsageAsync(
                    operationType: AIOperationType.Chat, provider: provider, model: model,
                    inputTokens: inputTokens, outputTokens: outputTokens, durationMs: durationMs,
                    isSuccess: isSuccess, errorMessage: errorMessage, agentId: agentId, threadId: threadId,
                    cachedInputTokens: cachedInputTokens, cacheCreationTokens: cacheCreationTokens, ct: ct);
            }
            catch (Exception retryEx)
            {
                _logger.LogWarning(retryEx, "Failed to log usage even with independent scope");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log usage");
        }
    }
}
