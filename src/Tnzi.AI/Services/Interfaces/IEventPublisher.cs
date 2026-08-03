namespace Tnzi.AI.Services;

public interface IEventPublisher
{
    Task PublishRunStartedEventAsync(AgentRunRequest request, AgentRun? run, bool isStreaming, string? provider, string? model, AgentExecutionMode executionMode);
    Task PublishRunCompletedEventAsync(AgentRunRequest request, AgentRunResult result, AgentRun? run, long durationMs, bool isStreaming, string? actualProvider);
    Task PublishRunFailedEventAsync(AgentRunRequest request, AgentRun? run, Exception exception, long durationMs, bool isStreaming);
    Task HandleNewThreadTitleAsync(AgentRunRequest request, AgentRunResult result);
}
