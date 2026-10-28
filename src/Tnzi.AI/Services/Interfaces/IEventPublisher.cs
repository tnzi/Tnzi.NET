namespace Tnzi.AI.Services;

public interface IEventPublisher
{
    Task PublishRunStartedEventAsync(AgentRunRequest request, AgentRun? run, bool isStreaming, string? provider, string? model, AgentExecutionMode executionMode);
    Task PublishRunCompletedEventAsync(AgentRunRequest request, AgentRunResult result, AgentRun? run, long durationMs, bool isStreaming, string? actualProvider);
    Task PublishRunFailedEventAsync(AgentRunRequest request, AgentRun? run, Exception exception, long durationMs, bool isStreaming);
    Task PublishRunCancelledEventAsync(AgentRunRequest request, AgentRun? run, string reason, long durationMs, bool isStreaming);
    /// <summary>
    /// 新线程首轮答完后给线程起标题（先写兜底标题，再发 <c>ThreadFirstReplyCompletedEvent</c> 交给 LLM 生成正式标题）。
    /// </summary>
    /// <param name="request">本轮请求</param>
    /// <param name="result">本轮结果</param>
    /// <param name="userTurnText">
    /// 本轮模型实际收到的用户文本（输入守卫改写之后；带内容块时是内容块里的文本）。为空时退回 <c>request.UserMessage</c>。
    /// </param>
    Task HandleNewThreadTitleAsync(AgentRunRequest request, AgentRunResult result, string? userTurnText = null);
}
