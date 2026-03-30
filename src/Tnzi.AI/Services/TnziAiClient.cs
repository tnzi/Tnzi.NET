using Tnzi.AI.Models;

namespace Tnzi.AI.Services;

/// <summary>
/// 嵌入式 AI 客户端实现 — 直接委托 IAgentRuntime + IAgentThreadService
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

        return new AiClientResponse
        {
            Text = result.Response,
            ThreadId = result.ThreadId,
            RunId = result.RunId,
            Usage = result.Usage,
            FinishReason = result.FinishReason,
            Citations = result.Citations
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
                Usage = chunk.Usage,
                IsToolCall = chunk.IsToolCall,
                Error = chunk.Error,
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
        return new AgentRunRequest
        {
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
}
