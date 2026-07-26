namespace Tnzi.AI.Services;

/// <summary>
/// 嵌入式 AI 客户端实现 - 直接委托 IAgentRuntime + IAgentThreadService
/// </summary>
public class TnziAiClient : ITnziAiClient
{
    private readonly IAgentRuntime _runtime;
    private readonly IAgentThreadService? _threadService;

    public TnziAiClient(IAgentRuntime runtime, IAgentThreadService? threadService = null)
    {
        _runtime = Check.NotNull(runtime);
        _threadService = threadService;
    }

    public async Task<AiClientResponse> ChatAsync(
        string message, Guid? threadId = null,
        AiClientOptions? options = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(message);

        var request = BuildRequest(message, threadId, options);
        var result = await _runtime.RunAsync(request, ct);
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

        await foreach (var chunk in _runtime.RunStreamingAsync(request, ct))
        {
            yield return new AiClientStreamEvent
            {
                Text = chunk.Text,
                FinishReason = chunk.FinishReason,
                Model = chunk.Model,
                Usage = chunk.Usage,
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
