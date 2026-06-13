namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 执行器抽象 — 工具调用循环引擎的公共契约。
/// <para>
/// 与契约类型（<see cref="AgentResolution"/>、<see cref="IAgentFactory"/>）同处一层，
/// 使 <c>Tnzi.AI.Workflow</c> 等下游程序集无需耦合具体实现 <c>AgentExecutor</c>；
/// 运行时与多 Agent 策略全部经本接口消费执行器（无具体类型转换），
/// 自定义实现/装饰器（日志、限流、测试替身）可安全替换。
/// </para>
/// </summary>
public interface IAgentExecutor
{
    /// <summary>
    /// Agent 名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 当前工具列表（只读）。默认实现返回空列表 — 不暴露工具的自定义执行器无需实现。
    /// </summary>
    IReadOnlyList<AITool> Tools => [];

    /// <summary>
    /// 非流式执行 — 驱动工具调用循环，返回完整响应。
    /// </summary>
    Task<AgentResponse> ExecuteAsync(List<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// 流式执行 — 驱动工具调用循环，逐块产出。
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(List<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// 创建一个合并了额外工具的新执行器（不修改原实例）。引擎运行时（中间件注入的技能/MCP 工具）
    /// 与多 Agent 策略（Handoff/Router/AgentAsTools）经此组合工具 — 全部通过本接口调用，
    /// 自定义 <see cref="IAgentExecutor"/> 实现不会再被强制转换为具体类型。
    /// 默认实现返回 <c>this</c>（不支持工具合并的执行器优雅降级：运行继续、仅缺少额外工具）。
    /// </summary>
    IAgentExecutor WithAdditionalTools(IEnumerable<AITool> additionalTools) => this;
}
