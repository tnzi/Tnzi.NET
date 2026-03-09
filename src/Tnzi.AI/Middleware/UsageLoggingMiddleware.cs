namespace Tnzi.AI.Middleware;

/// <summary>
/// 用量日志中间件 — After only: 记录 UsageLog + 发送 OTel 指标
/// </summary>
public class UsageLoggingMiddleware : IAiMiddleware
{
    private readonly IUsageLogService _usageLogService;
    private readonly ILogger<UsageLoggingMiddleware> _logger;

    public int Order => 500;

    public UsageLoggingMiddleware(
        IUsageLogService usageLogService,
        ILogger<UsageLoggingMiddleware> logger)
    {
        _usageLogService = Check.NotNull(usageLogService);
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
            await LogUsageSafeAsync(context, 0, 0, sw.ElapsedMilliseconds, false, ex.Message, CancellationToken.None);

            // 记录 OTel 错误指标
            AIActivitySource.RecordError(context.Agent.Provider, context.Agent.Model, ex.GetType().Name);

            throw;
        }

        sw.Stop();

        // After: 记录用量日志
        var inputTokens = result.Usage?.PromptTokens ?? 0;
        var outputTokens = result.Usage?.CompletionTokens ?? 0;

        await LogUsageSafeAsync(context, inputTokens, outputTokens, sw.ElapsedMilliseconds, isSuccess, errorMessage, cancellationToken);

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
            await LogUsageSafeAsync(context, 0, 0, sw.ElapsedMilliseconds, false, ex.Message, cancellationToken);
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
            var inputTokens = lastUsage?.PromptTokens ?? 0;
            var outputTokens = lastUsage?.CompletionTokens ?? 0;
            if (!completedNormally)
            {
                isSuccess = false;
                errorMessage = "Streaming was cancelled or failed";
            }
            await LogUsageSafeAsync(context, inputTokens, outputTokens, sw.ElapsedMilliseconds, isSuccess, errorMessage, CancellationToken.None);
            AIActivitySource.RecordChatRequest(context.Agent.Provider, context.Agent.Model, "chat_streaming");
            AIActivitySource.RecordTokenUsage(context.Agent.Provider, context.Agent.Model, inputTokens, outputTokens);
            AIActivitySource.RecordChatLatency(context.Agent.Provider, context.Agent.Model, sw.Elapsed.TotalSeconds);
        }
    }

    private async Task LogUsageSafeAsync(AiMiddlewareContext context, int inputTokens, int outputTokens, long durationMs, bool isSuccess, string? errorMessage, CancellationToken ct)
    {
        try
        {
            await _usageLogService.LogUsageAsync(
                operationType: AIOperationType.Chat,
                provider: context.Agent.Provider,
                model: context.Agent.Model ?? "unknown",
                inputTokens: inputTokens,
                outputTokens: outputTokens,
                durationMs: durationMs,
                isSuccess: isSuccess,
                errorMessage: errorMessage,
                agentId: context.Agent.AgentId,
                threadId: context.Request.ThreadId,
                ct: ct);
        }
        catch (Exception ex)
        {
            // 用量日志记录失败不应影响主流程
            _logger.LogWarning(ex, "Failed to log usage");
        }
    }
}
