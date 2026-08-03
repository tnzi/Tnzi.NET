namespace Tnzi.AI.Services;

/// <summary>
/// 统一 AI 运行入口。
/// 整合中间件管道 + 执行策略 + Run 追踪。
/// 所有 AI 执行（chat、workflow、agent run）都通过此入口。
/// </summary>
public interface IAgentRuntime
{
    /// <summary>执行一次 AI 运行（非流式）</summary>
    Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>执行一次 AI 运行（流式）</summary>
    IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>恢复被中断的运行</summary>
    Task<AgentRunResult> ResumeAsync(
        Guid runId,
        ResumeRunInput? input = null,
        CancellationToken cancellationToken = default);
}
