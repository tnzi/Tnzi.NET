namespace Tnzi.AI.Services;

/// <summary>
/// 嵌入式 AI 客户端实现 - 委托 IAgentDispatchFacade + IAgentThreadService
/// </summary>
/// <remarks>
/// 经路由门面而不是直调 <see cref="IAgentRuntime"/>：与 HTTP 聊天同一个分支点，绑定了外部 CLI 运行时的 Agent
/// 经嵌入式客户端调用时同样交给那个运行时。此前直调运行时，这类 Agent 会静默跑在内建引擎上，工具与模型都不同。
/// 没有绑定时门面原样交给运行时，行为不变。
/// </remarks>
public class TnziAiClient : ITnziAiClient
{
    private readonly IAgentDispatchFacade _dispatchFacade;
    private readonly IAgentThreadService? _threadService;

    public TnziAiClient(IAgentDispatchFacade dispatchFacade, IAgentThreadService? threadService = null)
    {
        _dispatchFacade = Check.NotNull(dispatchFacade);
        _threadService = threadService;
    }

    public async Task<AiClientResponse> ChatAsync(
        string message, Guid? threadId = null,
        AiClientOptions? options = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(message);

        var request = BuildRequest(message, threadId, options);
        var result = await _dispatchFacade.RunAsync(request, ct);
        ThrowIfFailed(result);

        return new AiClientResponse
        {
            Text = result.Response,
            ThreadId = result.ThreadId,
            RunId = result.RunId,
            Usage = result.Usage,
            FinishReason = result.FinishReason,
            Model = result.Model,
            Provider = result.Provider,
            Status = result.Status,
            Reasoning = result.Reasoning,
            Citations = result.Citations,
            HandoffPath = result.HandoffPath,
            FinalAgentName = result.FinalAgentName,
            Suggestions = result.Suggestions,
            Artifacts = result.Artifacts,
            ClarificationQuestion = result.ClarificationQuestion
        };
    }

    public async IAsyncEnumerable<AiClientStreamEvent> ChatStreamingAsync(
        string message, Guid? threadId = null,
        AiClientOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(message);

        var request = BuildRequest(message, threadId, options);

        // chunk 的 Usage 是一次 LLM 调用的增量（StreamUsage 口径）；对外事件只在终止事件上给出累计值，
        // 只带用量的 chunk 不单独成为事件
        TokenUsageDto? totalUsage = null;
        await foreach (var chunk in _dispatchFacade.RunStreamingAsync(request, ct))
        {
            totalUsage = StreamUsage.Add(totalUsage, chunk.Usage);
            if (StreamUsage.IsUsageOnly(chunk))
            {
                continue;
            }

            yield return new AiClientStreamEvent
            {
                Text = chunk.Text,
                FinishReason = chunk.FinishReason,
                Model = chunk.Model,
                Usage = chunk.FinishReason != null ? totalUsage : null,
                IsToolCall = chunk.IsToolCall,
                ToolCallNames = chunk.ToolCallNames,
                ToolCalls = chunk.ToolCalls,
                Error = chunk.Error,
                ReasoningText = chunk.ReasoningText,
                AgentName = chunk.AgentName,
                EventType = chunk.EventType,
                EventData = chunk.EventData,
                Suggestions = chunk.Suggestions,
                Todos = chunk.Todos,
                Artifacts = chunk.Artifacts,
                ThreadId = request.ThreadId
            };
        }
    }

    public async Task<Guid> CreateThreadAsync(string? title = null, CancellationToken ct = default)
    {
        if (_threadService == null)
            throw new InvalidOperationException("IAgentThreadService is not available. Thread creation requires the AI thread service.");

        var result = await _threadService.CreateAsync(new CreateAgentThreadDto { Title = title });
        if (!result.Succeeded || result.Data == null)
            throw new InvalidOperationException($"Failed to create thread: {result.Message}");

        return result.Data.Id;
    }

    public async Task DeleteThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        if (_threadService == null)
            throw new InvalidOperationException("IAgentThreadService is not available. Thread deletion requires the AI thread service.");

        var result = await _threadService.DeleteAsync(threadId);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to delete thread: {result.Message}");
    }

    private static AgentRunRequest BuildRequest(string message, Guid? threadId, AiClientOptions? options)
    {
        var operationType = options?.AgentId.HasValue == true
            ? AIOperationType.AgentRun
            : AIOperationType.Chat;

        return new AgentRunRequest
        {
            OperationType = operationType,
            UserMessage = message,
            ThreadId = threadId,
            AgentId = options?.AgentId,
            Provider = options?.Provider,
            Model = options?.Model,
            ToolGroups = options?.ToolGroups,
            // 嵌入式客户端的工具组来自应用代码，不是 HTTP 请求体
            TrustedToolSelection = true,
            EnableRunTracking = options?.EnableRunTracking ?? false,
            UserId = options?.UserId,
            StreamMode = options?.StreamMode ?? StreamMode.Messages
        };
    }

    private static void ThrowIfFailed(AgentRunResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Response) ? "AI request failed." : result.Response;

        switch (result.FinishReason)
        {
            case FinishReasons.QuotaExceeded:
                throw new BusinessException(message, ErrorCodes.QuotaExceeded, 429);
            case FinishReasons.GuardrailRejected:
                throw new BusinessException(message, ErrorCodes.GuardrailRejected, 400);
            case FinishReasons.Rejected:
                throw new BusinessException(message, ErrorCodes.AgentRunFailed, 400);
            case FinishReasons.MaxHandoffs:
            case FinishReasons.Error:
            case FinishReasons.Failed:
                throw new BusinessException(message, ErrorCodes.AgentRunFailed, 500);
        }
    }
}
