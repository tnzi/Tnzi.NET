
namespace Tnzi.AI.Engine.Strategies;

/// <summary>
/// 单 Agent 直通策略 — 默认行为，直接委托给 AgentExecutor
/// </summary>
public class SingleAgentStrategy : IExecutionStrategy
{
    /// <summary>全局单例（无状态，线程安全）</summary>
    public static readonly SingleAgentStrategy Instance = new();

    public async Task<ExecutionResult> ExecuteAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct)
    {
        var response = await agent.ExecuteAsync(messages, ct);
        return new ExecutionResult { Response = response };
    }

    public IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct)
    {
        return agent.ExecuteStreamingAsync(messages, ct);
    }
}
