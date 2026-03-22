namespace Tnzi.AI.Middleware;

/// <summary>
/// 历史中间件 — Before: 自动创建线程 + 加载对话历史, After: 保存用户消息和助手回复
/// </summary>
public class HistoryMiddleware : IAiMiddleware
{
    private readonly IAgentThreadInternalService _threadService;
    private readonly IOptions<AIOptions> _options;
    private readonly ILogger<HistoryMiddleware> _logger;

    public int Order => AiMiddlewareOrders.History;

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
        if (context.Agent.ExecutionMode == AgentExecutionMode.ExternalCli)
            return await next(context, cancellationToken);

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
        if (context.Agent.ExecutionMode == AgentExecutionMode.ExternalCli)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

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

        // 收集流式响应文本和工具调用详情
        var responseBuilder = new StringBuilder();
        var toolCallDetails = new List<ToolCallDetail>();
        TokenUsageDto? lastUsage = null;
        string? lastFinishReason = null;

        await foreach (var chunk in next(context, cancellationToken))
        {
            if (chunk.Text != null)
            {
                // Fix streaming token fracture: some providers (e.g., DeepSeek) insert
                // \n\n before each token after tool calls. Detect and replace with space.
                var text = chunk.Text;
                if (text.StartsWith("\n\n") && responseBuilder.Length > 0
                    && responseBuilder[^1] != '\n' && text.AsSpan().TrimStart().Length <= 15)
                {
                    text = " " + text.AsSpan(2).TrimStart().ToString();
                }
                responseBuilder.Append(text);
            }
            if (chunk.ToolCalls is { Count: > 0 })
            {
                toolCallDetails.AddRange(chunk.ToolCalls);
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
        // 使用 CancellationToken.None：流式结束后客户端可能已断开（token 已取消），
        // 但消息持久化必须完成，否则对话历史会丢失
        if (threadId != null && lastFinishReason != "guardrail_rejected")
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
                {
                    await _threadService.SaveMessageAsync(
                        threadId.Value, "user", context.Request.UserMessage, ct: CancellationToken.None);
                }

                var response = responseBuilder.ToString();
                if (!string.IsNullOrEmpty(response))
                {
                    var usageJson = lastUsage != null
                        ? JsonSerializer.Serialize(lastUsage)
                        : null;
                    var toolCallsJson = toolCallDetails.Count > 0
                        ? JsonSerializer.Serialize(toolCallDetails)
                        : null;

                    await _threadService.SaveMessageAsync(
                        threadId.Value, "assistant", response, toolCalls: toolCallsJson, usage: usageJson, ct: CancellationToken.None);
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
            var (_, resolvedThreadId, isNewThread) = await _threadService.GetOrCreateThreadAsync(null, context.Request.AgentId, ct);
            context.Request.ThreadId = resolvedThreadId;
            context.IsNewThread = isNewThread;
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
