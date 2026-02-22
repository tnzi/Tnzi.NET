namespace Tnzi.AI.Services;

/// <summary>
/// 聊天服务实现 - 支持多轮对话、日志记录、配额控制和错误处理
/// </summary>
public class ChatService : ApplicationService, IChatService
{
    private readonly ChatExecutionPipeline _pipeline;
    private readonly IOptions<AIOptions> _options;

    public ChatService(ChatExecutionPipeline pipeline, IOptions<AIOptions> options, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _pipeline = Check.NotNull(pipeline);
        _options = Check.NotNull(options);
    }

    public async Task<Result<ChatResponseDto>> ChatAsync(ChatRequestDto request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Guid? threadId = null;

        try
        {
            // 0. ThreadId 必须与 AgentId 一起使用（线程关联到 Agent）
            if (request.ThreadId.HasValue && !request.AgentId.HasValue)
            {
                return Fail<ChatResponseDto>("ThreadId requires AgentId for conversation thread.", 400, ErrorCodes.ThreadRequiresAgent);
            }

            // 1. 构建消息（支持多模态）
            var message = await _pipeline.BuildChatMessageAsync(request.Message, request.Content, ct);

            // 2. 原子预留配额
            var (reservation, quotaError) = await _pipeline.ReserveQuotaAsync<ChatResponseDto>(request.UserId, message, ct);
            if (quotaError != null) return quotaError;

            // 3. 解析 Agent
            var resolution = await _pipeline.ResolveAgentAsync(request.AgentId, request.Provider, request.Model, request.ToolGroups, ct);
            if (!resolution.IsSuccess)
            {
                return resolution.ErrorCode == ErrorCodes.AgentDisabled
                    ? Fail<ChatResponseDto>("Agent is disabled", 400, ErrorCodes.AgentDisabled)
                    : Fail<ChatResponseDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
            }

            // 4. 获取或创建线程
            var (context, resolvedThreadId) = request.AgentId.HasValue
                ? await _pipeline.PrepareThreadAsync(request.ThreadId, request.AgentId.Value, ct)
                : (null, (Guid?)null);
            threadId = resolvedThreadId;

            // 5. 执行对话
            var response = await _pipeline.ExecuteAsync(resolution.Agent!, message, context, threadId, ct);

            // 6. 持久化消息历史
            await _pipeline.PersistAfterRunAsync(threadId, context, message, response.Text ?? string.Empty, ct);

            // 7. 记录使用日志
            var usage = response.Usage;
            var actualTokens = (int)(usage?.TotalTokenCount ?? 0);
            await _pipeline.LogUsageAsync(
                AIOperationType.Chat,
                resolution.Provider,
                resolution.Model ?? "default",
                (int)(usage?.InputTokenCount ?? 0),
                (int)(usage?.OutputTokenCount ?? 0),
                stopwatch.ElapsedMilliseconds,
                true,
                agentId: resolution.AgentId,
                threadId: threadId,
                ct: ct);

            // 8. 结算配额（调整预估与实际的差值）
            await _pipeline.SettleQuotaAsync(request.UserId, reservation, actualTokens, ct);

            return Ok(new ChatResponseDto
            {
                Content = response.Text ?? string.Empty,
                Model = resolution.Model,
                Usage = ChatExecutionPipeline.MapUsage(response.Usage),
                ThreadId = threadId
            });
        }
        catch (BusinessException ex)
        {
            Logger.LogWarning(ex,
                "Chat request failed with business exception: Provider={Provider}, Model={Model}, UserId={UserId}, ThreadId={ThreadId}",
                request.Provider ?? _options.Value.DefaultProvider,
                request.Model ?? "default",
                request.UserId,
                request.ThreadId);

            return Fail<ChatResponseDto>(ex.Message, ex.HttpStatusCode, ex.Code);
        }
        catch (InvalidOperationException ex) when (ex.Message?.Contains("quota", StringComparison.OrdinalIgnoreCase) == true)
        {
            Logger.LogWarning("Chat quota exceeded: UserId={UserId}, Message={Message}", request.UserId, ex.Message);
            return Fail<ChatResponseDto>(ex.Message, 429, ErrorCodes.QuotaExceeded);
        }
        catch (Exception ex)
        {
            var fallbackProvider = request.Provider ?? _options.Value.DefaultProvider;
            var fallbackModel = request.Model ?? "default";
            Logger.LogError(ex,
                "Chat request failed: Provider={Provider}, Model={Model}, UserId={UserId}, ThreadId={ThreadId}, Message={Message}",
                fallbackProvider, fallbackModel, request.UserId, request.ThreadId, ex.Message);

            await _pipeline.LogUsageAsync(
                AIOperationType.Chat,
                fallbackProvider,
                fallbackModel,
                0, 0, stopwatch.ElapsedMilliseconds,
                false,
                errorMessage: ex.Message,
                threadId: threadId,
                ct: ct);

            return Fail<ChatResponseDto>($"Chat failed: {ex.Message}", 500, ErrorCodes.ChatFailed);
        }
    }

    public async IAsyncEnumerable<StreamEvent> ChatStreamingAsync(ChatRequestDto request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var contentBuilder = new StringBuilder();
        int inputTokens = 0;
        int outputTokens = 0;

        // 0. ThreadId 必须与 AgentId 一起使用
        if (request.ThreadId.HasValue && !request.AgentId.HasValue)
        {
            throw new BusinessException("ThreadId requires AgentId for conversation thread.", ErrorCodes.ThreadRequiresAgent, 400);
        }

        // 1. 构建消息（支持多模态）
        var message = await _pipeline.BuildChatMessageAsync(request.Message, request.Content, ct);

        // 2. 原子预留配额
        var reservation = await _pipeline.ReserveQuotaOrThrowAsync(request.UserId, message, ct);

        // 3. 解析 Agent
        var resolution = await _pipeline.ResolveAgentAsync(request.AgentId, request.Provider, request.Model, request.ToolGroups, ct);
        if (!resolution.IsSuccess)
        {
            var errorMsg = resolution.ErrorCode == ErrorCodes.AgentDisabled ? "Agent is disabled" : "Agent not found";
            var statusCode = resolution.ErrorCode == ErrorCodes.AgentDisabled ? 400 : 404;
            throw new BusinessException(errorMsg, resolution.ErrorCode ?? ErrorCodes.AgentNotFound, statusCode);
        }

        // 4. 获取或创建线程
        ConversationContext? context = null;
        if (request.ThreadId.HasValue && request.AgentId.HasValue)
        {
            (context, _) = await _pipeline.PrepareThreadAsync(request.ThreadId, request.AgentId.Value, ct);
        }

        // 5. 流式执行（delta 模型 — 每个事件只包含增量内容）
        AgentStreamChunk? lastChunk = null;
        await foreach (var chunk in _pipeline.ExecuteStreamingAsync(resolution.Agent!, message, context, request.ThreadId, ct).WithCancellation(ct))
        {
            if (chunk.Text != null)
            {
                contentBuilder.Append(chunk.Text);
            }

            lastChunk = chunk;

            // 提取 Token 使用信息
            var (inp, outp) = ChatExecutionPipeline.ExtractStreamingUsage(chunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }

            // 仅在有增量文本时发送 delta 事件
            if (chunk.Text != null)
            {
                yield return new StreamEvent
                {
                    Delta = chunk.Text,
                    Model = resolution.Model,
                    ThreadId = request.ThreadId
                };
            }
        }

        // 如果流式响应结束时还没有 Token 信息，尝试从最后一条 chunk 中查找
        if (lastChunk is not null && inputTokens == 0 && outputTokens == 0)
        {
            var (inp, outp) = ChatExecutionPipeline.ExtractStreamingUsage(lastChunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }
        }

        // 发送终止事件（含 Usage 和 FinishReason）
        yield return new StreamEvent
        {
            IsDone = true,
            FinishReason = "stop",
            Model = resolution.Model,
            ThreadId = request.ThreadId,
            Usage = new TokenUsageDto
            {
                PromptTokens = inputTokens,
                CompletionTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens
            }
        };

        // 6. 持久化历史与线程序列化数据
        await _pipeline.PersistAfterRunAsync(request.ThreadId, context, message, contentBuilder.ToString(), ct);

        // 7. 记录日志
        await _pipeline.LogUsageAsync(
            AIOperationType.ChatStreaming,
            resolution.Provider,
            resolution.Model ?? "default",
            inputTokens,
            outputTokens,
            stopwatch.ElapsedMilliseconds,
            true,
            agentId: resolution.AgentId,
            threadId: request.ThreadId,
            ct: ct);

        // 8. 结算配额
        var totalTokens = inputTokens + outputTokens;
        await _pipeline.SettleQuotaAsync(request.UserId, reservation, totalTokens, ct);
    }
}
