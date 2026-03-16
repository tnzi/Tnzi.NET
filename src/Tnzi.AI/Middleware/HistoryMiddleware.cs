namespace Tnzi.AI.Middleware;

/// <summary>
/// 历史中间件 — Before: 自动创建线程 + 加载对话历史, After: 保存用户消息和助手回复
/// </summary>
public class HistoryMiddleware : IAiMiddleware
{
    private readonly IAgentThreadInternalService _threadService;
    private readonly IOptions<AIOptions> _options;
    private readonly ILogger<HistoryMiddleware> _logger;

    public int Order => 300;

    public HistoryMiddleware(
        IAgentThreadInternalService threadService,
        IOptions<AIOptions> options,
        ILogger<HistoryMiddleware> logger)
    {
        _threadService = Check.NotNull(threadService);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        // Before: 自动创建线程（如果 ThreadId 为 null）
        await EnsureThreadAsync(context, cancellationToken);

        var threadId = context.Request.ThreadId;

        // Before: 加载对话历史
        if (threadId != null)
        {
            try
            {
                var maxLoaded = _options.Value.History.Store.MaxLoadedMessages;
                var history = await _threadService.GetMessageHistoryAsync(threadId.Value, limit: maxLoaded, ct: cancellationToken);
                if (history.Count > 0)
                {
                    // 将历史消息插入到 Messages 前面
                    context.Messages.InsertRange(0, history);
                    _logger.LogDebug("Loaded {Count} history messages for thread {ThreadId}", history.Count, threadId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load history for thread {ThreadId}", threadId);
            }
        }

        // 执行下游管道
        var result = await next(context, cancellationToken);

        // 确保结果携带 ThreadId
        if (threadId != null && result.ThreadId == null)
        {
            result = new AgentRunResult
            {
                Response = result.Response,
                RunId = result.RunId,
                ThreadId = threadId,
                Usage = result.Usage,
                Citations = result.Citations,
                FinishReason = result.FinishReason,
                Status = result.Status
            };
        }

        // After: 保存消息（guardrail 拒绝时不保存，避免将被拦截的内容写入历史）
        if (threadId != null && result.FinishReason != "guardrail_rejected")
        {
            try
            {
                // 保存用户消息
                if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
                {
                    await _threadService.SaveMessageAsync(
                        threadId.Value,
                        "user",
                        context.Request.UserMessage,
                        ct: cancellationToken);
                }

                // 保存助手回复
                if (!string.IsNullOrEmpty(result.Response))
                {
                    var usageJson = result.Usage != null
                        ? JsonSerializer.Serialize(result.Usage)
                        : null;

                    await _threadService.SaveMessageAsync(
                        threadId.Value,
                        "assistant",
                        result.Response,
                        usage: usageJson,
                        ct: cancellationToken);
                }

                _logger.LogDebug("Persisted messages for thread {ThreadId}", threadId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist messages for thread {ThreadId}", threadId);
            }
        }

        return result;
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Before: 自动创建线程（如果 ThreadId 为 null）
        await EnsureThreadAsync(context, cancellationToken);

        var threadId = context.Request.ThreadId;

        // Before: 加载对话历史
        if (threadId != null)
        {
            try
            {
                var maxLoaded = _options.Value.History.Store.MaxLoadedMessages;
                var history = await _threadService.GetMessageHistoryAsync(threadId.Value, limit: maxLoaded, ct: cancellationToken);
                if (history.Count > 0)
                {
                    context.Messages.InsertRange(0, history);
                    _logger.LogDebug("Loaded {Count} history messages for thread {ThreadId}", history.Count, threadId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load history for thread {ThreadId}", threadId);
            }
        }

        // 收集流式响应文本
        var responseBuilder = new StringBuilder();
        TokenUsageDto? lastUsage = null;
        string? lastFinishReason = null;

        await foreach (var chunk in next(context, cancellationToken))
        {
            if (chunk.Text != null)
            {
                responseBuilder.Append(chunk.Text);
            }
            if (chunk.Usage != null)
            {
                lastUsage = chunk.Usage;
            }
            if (chunk.FinishReason != null)
            {
                lastFinishReason = chunk.FinishReason;
            }
            yield return chunk;
        }

        // After: 保存消息（guardrail 拒绝时不保存，避免将被拦截的内容写入历史）
        if (threadId != null && lastFinishReason != "guardrail_rejected")
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
                {
                    await _threadService.SaveMessageAsync(
                        threadId.Value, "user", context.Request.UserMessage, ct: cancellationToken);
                }

                var response = responseBuilder.ToString();
                if (!string.IsNullOrEmpty(response))
                {
                    var usageJson = lastUsage != null
                        ? JsonSerializer.Serialize(lastUsage)
                        : null;

                    await _threadService.SaveMessageAsync(
                        threadId.Value, "assistant", response, usage: usageJson, ct: cancellationToken);
                }

                _logger.LogDebug("Persisted streaming messages for thread {ThreadId}", threadId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist streaming messages for thread {ThreadId}", threadId);
            }
        }
    }

    /// <summary>
    /// 当 ThreadId 为 null 时，自动创建新线程并写回 Request.ThreadId
    /// </summary>
    private async Task EnsureThreadAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        if (context.Request.ThreadId != null) return;

        try
        {
            var (_, resolvedThreadId) = await _threadService.GetOrCreateThreadAsync(null, context.Request.AgentId, ct);
            context.Request.ThreadId = resolvedThreadId;
            _logger.LogDebug("Auto-created thread {ThreadId} for agent {AgentId}", resolvedThreadId, context.Request.AgentId);
        }
        catch (BusinessException)
        {
            throw; // Agent 不存在等业务异常应向上传播
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-create thread for agent {AgentId}", context.Request.AgentId);
        }
    }
}
