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
        if (context.ShouldSkipMiddleware)
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

            // Patch dangling tool calls (from user interruption)
            var patched = PatchDanglingToolCalls(context.Messages);
            if (!ReferenceEquals(patched, context.Messages))
            {
                context.Messages.Clear();
                context.Messages.AddRange(patched);
                _logger.LogDebug("Patched dangling tool calls in thread {ThreadId}", threadId);
            }
        }

        // 执行下游管道
        var result = await next(context, cancellationToken);

        // 确保结果携带 ThreadId
        if (threadId != null && result.ThreadId == null)
        {
            result = result.CloneWith(threadId: threadId);
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
        if (context.ShouldSkipMiddleware)
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

            // Patch dangling tool calls (from user interruption)
            var patched = PatchDanglingToolCalls(context.Messages);
            if (!ReferenceEquals(patched, context.Messages))
            {
                context.Messages.Clear();
                context.Messages.AddRange(patched);
                _logger.LogDebug("Patched dangling tool calls in thread {ThreadId}", threadId);
            }
        }

        // 收集流式响应文本和工具调用详情
        var responseBuilder = new StringBuilder();
        var toolCallDetails = new List<ToolCallDetail>();
        TokenUsageDto? lastUsage = null;
        string? lastFinishReason = null;
        var afterToolCall = false;

        await foreach (var chunk in next(context, cancellationToken))
        {
            // 追踪工具调用状态，用于检测 provider 在 tool call 后注入 \n\n 的问题
            var isToolCallChunk = chunk.IsToolCall || chunk.ToolCalls is { Count: > 0 };
            if (isToolCallChunk)
            {
                afterToolCall = true;
            }

            if (chunk.Text != null)
            {
                var text = chunk.Text;

                // Fix streaming token fracture: some providers (e.g., DeepSeek) insert
                // \n\n before each token after tool calls. Detect and replace with space.
                // 仅在 tool call 后的纯文本 chunk 触发，避免误清理正常的段落换行。
                if (afterToolCall && !isToolCallChunk
                    && text.StartsWith("\n\n") && responseBuilder.Length > 0
                    && responseBuilder[^1] != '\n' && text.AsSpan().TrimStart().Length <= 15)
                {
                    text = " " + text.AsSpan(2).TrimStart().ToString();
                    chunk.Text = text;
                }
                else if (afterToolCall && !isToolCallChunk && !text.StartsWith("\n\n"))
                {
                    // 纯文本 chunk 不以 \n\n 开头 → 正常文本流恢复，重置状态
                    afterToolCall = false;
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
    /// Scans messages for FunctionCallContent without matching FunctionResultContent.
    /// Injects synthetic error results for dangling calls (e.g., from user interruption).
    /// </summary>
    public static List<ChatMessage> PatchDanglingToolCalls(List<ChatMessage> messages)
    {
        // Collect all existing result call IDs
        var existingResultIds = new HashSet<string>();
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent frc)
                    existingResultIds.Add(frc.CallId);
            }
        }

        // Find dangling calls and track insertion points
        var patches = new List<(int InsertAfterIndex, List<FunctionResultContent> Results)>();

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.Assistant) continue;

            var danglingResults = new List<FunctionResultContent>();
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc && !existingResultIds.Contains(fc.CallId))
                {
                    danglingResults.Add(new FunctionResultContent(
                        fc.CallId,
                        "[Tool call was interrupted and did not return a result.]"));
                    existingResultIds.Add(fc.CallId); // Prevent duplicates
                }
            }

            if (danglingResults.Count > 0)
                patches.Add((i, danglingResults));
        }

        if (patches.Count == 0)
            return messages;

        // Build patched list with synthetic results inserted after each dangling assistant message
        var patched = new List<ChatMessage>(messages.Count + patches.Sum(p => p.Results.Count));
        var patchIndex = 0;

        for (var i = 0; i < messages.Count; i++)
        {
            patched.Add(messages[i]);

            if (patchIndex < patches.Count && patches[patchIndex].InsertAfterIndex == i)
            {
                foreach (var result in patches[patchIndex].Results)
                {
                    patched.Add(new ChatMessage(ChatRole.Tool, [result]));
                }
                patchIndex++;
            }
        }

        return patched;
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
