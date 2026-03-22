
namespace Tnzi.AI.Engine.Strategies;

/// <summary>
/// 执行策略接口 — Pipeline 内可替换的 Agent 执行策略
/// </summary>
public interface IExecutionStrategy
{
    /// <summary>
    /// 非流式执行
    /// </summary>
    Task<ExecutionResult> ExecuteAsync(AgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct);

    /// <summary>
    /// 流式执行
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(AgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct);
}

/// <summary>
/// 执行策略结果（包含多 Agent 编排元数据）
/// </summary>
public class ExecutionResult
{
    /// <summary>最终 Agent 的响应</summary>
    public required AgentResponse Response { get; init; }

    /// <summary>执行路径（handoff/router/supervisor/groupchat 等多 Agent 模式可用）</summary>
    public List<string>? HandoffPath { get; init; }

    /// <summary>最终处理对话的 Agent 名称</summary>
    public string? FinalAgentName { get; init; }

    /// <summary>聚合 Token 使用量（多 hop 累加）</summary>
    public TokenUsageDto? AggregatedUsage { get; init; }
}

/// <summary>
/// 执行策略上下文 — 提供策略所需的依赖
/// </summary>
public class ExecutionStrategyContext
{
    /// <summary>Agent 工厂（用于创建新的 AgentExecutor）</summary>
    public required IAgentFactory AgentFactory { get; init; }

    /// <summary>Agent 仓储（用于按 ID 加载 Agent 实体）</summary>
    public required IRepository<Agent, Guid> AgentRepository { get; init; }

    /// <summary>服务提供者</summary>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>日志记录器</summary>
    public required ILogger Logger { get; init; }

    /// <summary>起始 Agent 的实体 ID（用于双向 handoff 来源追踪）</summary>
    public Guid? StartingAgentId { get; init; }
}
