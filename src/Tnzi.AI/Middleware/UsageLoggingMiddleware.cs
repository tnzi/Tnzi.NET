namespace Tnzi.AI.Middleware;

/// <summary>
/// 用量日志中间件 - After only: 记录 UsageLog + 发送 OTel 指标
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
        var operationType = ResolveOperationType(context.Request, streaming: false);

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
            var failureProvider = context.EffectiveProvider ?? context.Agent.Provider;
            var failureModel = context.EffectiveModel ?? context.Agent.Model;

            // 记录失败日志（用 CancellationToken.None，原 token 可能已取消）
            await LogUsageSafeAsync(operationType, failureProvider, failureModel, context, 0, 0, 0, 0, sw.ElapsedMilliseconds, false, ex.Message, CancellationToken.None);

            // 记录 OTel 错误指标
            AIActivitySource.RecordError(failureProvider, failureModel, ex.GetType().Name);

            throw;
        }

        sw.Stop();
        if (IsUnsuccessfulFinishReason(result.FinishReason))
        {
            isSuccess = false;
            errorMessage = BuildFailureMessage(result.FinishReason, result.Response);
        }

        // After: 记录用量日志
        var inputTokens = result.Usage?.InputTokens ?? 0;
        var outputTokens = result.Usage?.OutputTokens ?? 0;
        var cachedInputTokens = result.Usage?.CachedInputTokens ?? 0;
        var cacheCreationTokens = result.Usage?.CacheCreationTokens ?? 0;
        var actualProvider = result.Provider ?? context.EffectiveProvider ?? context.Agent.Provider;
        var actualModel = result.Model ?? context.EffectiveModel ?? context.Agent.Model;

        await LogUsageSafeAsync(operationType, actualProvider, actualModel, context, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens, sw.ElapsedMilliseconds, isSuccess, errorMessage, cancellationToken);

        // 记录 OTel 指标
        AIActivitySource.RecordChatRequest(actualProvider, actualModel, operationType);
        AIActivitySource.RecordTokenUsage(actualProvider, actualModel, inputTokens, outputTokens, operationType);
        AIActivitySource.RecordChatLatency(actualProvider, actualModel, sw.Elapsed.TotalSeconds, operationType);

        return result;
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        TokenUsageDto? lastUsage = null;
        var isSuccess = true;
        string? errorMessage = null;
        string? lastFinishReason = null;
        string? lastModel = null;
        var actualProvider = context.EffectiveProvider ?? context.Agent.Provider;
        var operationType = ResolveOperationType(context.Request, streaming: true);

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
            var failureModel = context.EffectiveModel ?? context.Agent.Model;
            await LogUsageSafeAsync(operationType, actualProvider, failureModel, context, 0, 0, 0, 0, sw.ElapsedMilliseconds, false, ex.Message, cancellationToken);
            AIActivitySource.RecordError(actualProvider, failureModel, ex.GetType().Name);
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
                if (!string.IsNullOrWhiteSpace(chunk.FinishReason))
                {
                    lastFinishReason = chunk.FinishReason;
                }
                if (!string.IsNullOrWhiteSpace(chunk.Model))
                {
                    lastModel = chunk.Model;
                }
                if (!string.IsNullOrWhiteSpace(chunk.Error))
                {
                    errorMessage = chunk.Error;
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
            else if (IsUnsuccessfulFinishReason(lastFinishReason))
            {
                isSuccess = false;
                errorMessage = BuildFailureMessage(lastFinishReason, errorMessage);
            }
            var cachedInputTokens = lastUsage?.CachedInputTokens ?? 0;
            var cacheCreationTokens = lastUsage?.CacheCreationTokens ?? 0;
            var actualModel = lastModel ?? context.EffectiveModel ?? context.Agent.Model;
            await LogUsageSafeAsync(operationType, actualProvider, actualModel, context, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens, sw.ElapsedMilliseconds, isSuccess, errorMessage, CancellationToken.None);
            AIActivitySource.RecordChatRequest(actualProvider, actualModel, operationType);
            AIActivitySource.RecordTokenUsage(actualProvider, actualModel, inputTokens, outputTokens, operationType);
            AIActivitySource.RecordChatLatency(actualProvider, actualModel, sw.Elapsed.TotalSeconds, operationType);
        }
    }

    private static string ResolveOperationType(AgentRunRequest request, bool streaming)
    {
        var operationType = request.OperationType;
        if (string.IsNullOrWhiteSpace(operationType))
        {
            operationType = request.WorkflowId.HasValue
                ? AIOperationType.WorkflowRun
                : request.AgentId.HasValue
                    ? AIOperationType.AgentRun
                    : AIOperationType.Chat;
        }

        if (!streaming)
        {
            return operationType;
        }

        return operationType switch
        {
            AIOperationType.Chat => AIOperationType.ChatStreaming,
            AIOperationType.AgentRun => AIOperationType.AgentRunStreaming,
            AIOperationType.WorkflowRun => AIOperationType.WorkflowRunStreaming,
            _ => operationType
        };
    }

    private static bool IsUnsuccessfulFinishReason(string? finishReason)
    {
        return finishReason switch
        {
            null or "" => false,
            FinishReasons.Stop or
            FinishReasons.Completed or
            FinishReasons.AgentAsToolsComplete => false,
            _ => true
        };
    }

    private static string BuildFailureMessage(string? finishReason, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return finishReason switch
        {
            null or "" => "AI request failed.",
            _ => $"AI request finished unsuccessfully: {finishReason}"
        };
    }

    /// <summary>
    /// 安全记录用量日志。streaming 场景下请求 scope 可能已释放，
    /// 因此先尝试已注入的 service，失败后用独立 scope 重试。
    /// </summary>
    private async Task LogUsageSafeAsync(string operationType, string provider, string? model, AiMiddlewareContext context, int inputTokens, int outputTokens, int cachedInputTokens, int cacheCreationTokens, long durationMs, bool isSuccess, string? errorMessage, CancellationToken ct)
    {
        var agentId = context.Agent.AgentId;
        var threadId = context.Request.ThreadId;
        model ??= "unknown";

        _logger.LogInformation(
            "AI usage: provider={Provider} model={Model} input={Input} output={Output} cached={Cached} creation={Creation} duration={Duration}ms success={Success}",
            provider, model, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens, durationMs, isSuccess);

        try
        {
            await _usageLogService.LogUsageAsync(
                operationType: operationType, provider: provider, model: model,
                inputTokens: inputTokens, outputTokens: outputTokens, durationMs: durationMs,
                isSuccess: isSuccess, errorMessage: errorMessage, agentId: agentId, threadId: threadId,
                cachedInputTokens: cachedInputTokens, cacheCreationTokens: cacheCreationTokens, ct: ct);
        }
        catch (ObjectDisposedException)
        {
            // Request scope already disposed (streaming scenario) - retry with independent scope
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<IUsageLogService>();
                await scopedService.LogUsageAsync(
                    operationType: operationType, provider: provider, model: model,
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
