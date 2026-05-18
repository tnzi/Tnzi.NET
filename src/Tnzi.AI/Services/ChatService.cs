namespace Tnzi.AI.Services;

/// <summary>
/// 聊天服务实现 — 委托给 IAgentRuntime 执行，Quota/Guardrails/History/UsageLogging 全部由中间件处理
/// </summary>
public class ChatService : ApplicationService, IChatService
{
    private readonly IAgentRuntime _runtime;
    private readonly IAgentResolver _agentResolver;
    private readonly IOptions<AIOptions> _options;

    public ChatService(
        IAgentRuntime runtime,
        IAgentResolver agentResolver,
        IOptions<AIOptions> options,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _runtime = Check.NotNull(runtime);
        _agentResolver = Check.NotNull(agentResolver);
        _options = Check.NotNull(options);
    }

    public async Task<Result<ChatResponseDto>> ChatAsync(ChatRequestDto request, CancellationToken ct = default)
    {
        try
        {
            // 构建 AgentRunRequest 并委托给 Runtime
            var runRequest = new AgentRunRequest
            {
                OperationType = AIOperationType.Chat,
                AgentId = request.AgentId,
                Provider = request.Provider,
                Model = request.Model,
                ToolGroups = request.ToolGroups,
                UserMessage = request.Message,
                ContentParts = request.Content,
                ThreadId = request.ThreadId,
                UserId = request.UserId,
                ReasoningEffort = request.ReasoningEffort
            };

            var result = await _runtime.RunAsync(runRequest, ct);
            if (TryMapFailure(result, out var statusCode, out var errorCode))
            {
                return Fail<ChatResponseDto>(result.Response, statusCode, errorCode);
            }

            return Ok(new ChatResponseDto
            {
                Content = result.Response,
                FinishReason = result.FinishReason,
                Model = result.Model ?? request.Model,
                Usage = result.Usage,
                ThreadId = result.ThreadId,
                Citations = result.Citations,
                HandoffPath = result.HandoffPath,
                Reasoning = result.Reasoning,
                UserMessageId = result.UserMessageId,
                AssistantMessageId = result.AssistantMessageId
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
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Chat request failed: Provider={Provider}, Model={Model}, UserId={UserId}, ThreadId={ThreadId}",
                request.Provider ?? _options.Value.DefaultProvider,
                request.Model ?? "default",
                request.UserId,
                request.ThreadId);

            return Fail<ChatResponseDto>("Chat request failed.", 500, ErrorCodes.ChatFailed);
        }
    }

    private static bool TryMapFailure(AgentRunResult result, out int statusCode, out string errorCode)
    {
        switch (result.FinishReason)
        {
            case FinishReasons.QuotaExceeded:
                statusCode = 429;
                errorCode = ErrorCodes.QuotaExceeded;
                return true;
            case FinishReasons.GuardrailRejected:
                statusCode = 400;
                errorCode = ErrorCodes.GuardrailRejected;
                return true;
            case FinishReasons.Rejected:
                statusCode = 400;
                errorCode = ErrorCodes.ChatFailed;
                return true;
            case FinishReasons.MaxHandoffs:
            case FinishReasons.Error:
            case FinishReasons.Failed:
                statusCode = 500;
                errorCode = ErrorCodes.ChatFailed;
                return true;
            default:
                statusCode = 0;
                errorCode = string.Empty;
                return false;
        }
    }

    public async IAsyncEnumerable<StreamEvent> ChatStreamingAsync(ChatRequestDto request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        int inputTokens = 0;
        int outputTokens = 0;
        Guid? userMessageId = null;
        Guid? assistantMessageId = null;

        // 构建 AgentRunRequest 并委托给 Runtime
        var runRequest = new AgentRunRequest
        {
            OperationType = AIOperationType.Chat,
            AgentId = request.AgentId,
            Provider = request.Provider,
            Model = request.Model,
            ToolGroups = request.ToolGroups,
            UserMessage = request.Message,
            ContentParts = request.Content,
            ThreadId = request.ThreadId,
            UserId = request.UserId,
            ReasoningEffort = request.ReasoningEffort
        };

        AgentStreamChunk? lastChunk = null;
        string? streamErrorMessage = null;
        string? streamFinishReason = null;
        string? streamModel = request.Model;
        List<CitationDto>? citations = null;
        bool earlyDoneSent = false;

        await foreach (var chunk in _runtime.RunStreamingAsync(runRequest, ct).WithCancellation(ct))
        {
            lastChunk = chunk;

            // HistoryMiddleware 在首批 chunk 到达前已完成线程自动创建（EnsureThreadAsync），
            // 此时 runRequest.ThreadId 已被更新为真实 ThreadId，直接从 runRequest 读取。
            var currentThreadId = runRequest.ThreadId;

            // 提取 Token 使用信息
            var (inp, outp) = ChatMessageHelper.ExtractStreamingUsage(chunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }

            // 收集 Citations（通常只在最终 chunk 中包含）
            if (chunk.Citations != null)
            {
                citations = chunk.Citations;
            }
            if (!string.IsNullOrWhiteSpace(chunk.Model))
            {
                streamModel = chunk.Model;
            }

            // 发送 delta 事件、工具调用状态事件或错误事件（guardrail 拦截等）
            if (chunk.Error != null)
            {
                streamErrorMessage = chunk.Error;
                streamFinishReason = chunk.FinishReason;
            }

            // Capture persisted message ids stamped onto the terminal chunk by HistoryMiddleware.
            if (chunk.UserMessageId.HasValue) userMessageId = chunk.UserMessageId;
            if (chunk.AssistantMessageId.HasValue) assistantMessageId = chunk.AssistantMessageId;

            var streamEvent = CreateStreamEvent(chunk, currentThreadId, ErrorCodes.ChatFailed);
            if (streamEvent != null)
            {
                yield return streamEvent;
            }

            // Yield isDone early when ANY terminal FinishReason is detected, so the client
            // receives it BEFORE the next MoveNextAsync() triggers middleware cleanup (DB writes).
            // All non-null FinishReasons are terminal for the current stream.
            if (!earlyDoneSent && chunk.FinishReason != null)
            {
                earlyDoneSent = true;
                var hasError = streamErrorMessage != null;
                yield return new StreamEvent
                {
                    IsDone = true,
                    FinishReason = chunk.FinishReason,
                    IsError = hasError,
                    ErrorMessage = hasError ? streamErrorMessage : null,
                    Model = streamModel,
                    ThreadId = currentThreadId,
                    Usage = new TokenUsageDto
                    {
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                        TotalTokens = inputTokens + outputTokens
                    },
                    Citations = citations,
                    UserMessageId = userMessageId,
                    AssistantMessageId = assistantMessageId
                };
            }
        }

        // Fallback: send isDone if it wasn't already sent early (e.g., error paths or missing FinishReason)
        if (!earlyDoneSent)
        {
            // 如果流式响应结束时还没有 Token 信息，尝试从最后一条 chunk 中查找
            if (lastChunk is not null && inputTokens == 0 && outputTokens == 0)
            {
                var (inp2, outp2) = ChatMessageHelper.ExtractStreamingUsage(lastChunk);
                if (inp2 > 0 || outp2 > 0) { inputTokens = inp2; outputTokens = outp2; }
            }

            var hasStreamError = streamErrorMessage != null;
            yield return new StreamEvent
            {
                IsDone = true,
                FinishReason = hasStreamError ? (streamFinishReason ?? "error") : "stop",
                Model = streamModel,
                ThreadId = runRequest.ThreadId,
                IsError = hasStreamError,
                ErrorMessage = hasStreamError ? streamErrorMessage : null,
                Usage = new TokenUsageDto
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    TotalTokens = inputTokens + outputTokens
                },
                Citations = citations,
                UserMessageId = userMessageId,
                AssistantMessageId = assistantMessageId
            };
        }
    }

    private static StreamEvent? CreateStreamEvent(AgentStreamChunk chunk, Guid? threadId, string defaultErrorCode)
    {
        var hasPayload =
            chunk.Error != null ||
            chunk.AgentName != null ||
            chunk.ReasoningText != null ||
            chunk.Text != null ||
            chunk.ToolCalls != null ||
            chunk.IsToolCall ||
            chunk.ToolCallNames != null ||
            chunk.EventType != null ||
            chunk.EventData != null ||
            chunk.Suggestions != null ||
            chunk.Todos != null ||
            chunk.Artifacts != null;

        if (!hasPayload)
        {
            return null;
        }

        return new StreamEvent
        {
            Delta = chunk.Text,
            FinishReason = chunk.FinishReason,
            Model = chunk.Model,
            ThreadId = threadId,
            IsError = chunk.Error != null,
            ErrorMessage = chunk.Error,
            ErrorCode = chunk.Error != null && chunk.FinishReason == FinishReasons.GuardrailRejected
                ? ErrorCodes.GuardrailRejected
                : chunk.Error != null
                    ? defaultErrorCode
                    : null,
            IsToolCall = chunk.IsToolCall,
            ToolCallNames = chunk.ToolCallNames,
            ReasoningDelta = chunk.ReasoningText,
            AgentName = chunk.AgentName,
            ToolCalls = chunk.ToolCalls,
            EventType = chunk.EventType,
            EventData = chunk.EventData,
            Suggestions = chunk.Suggestions,
            Todos = chunk.Todos,
            Artifacts = chunk.Artifacts
        };
    }
}
