
namespace Tnzi.AI.Engine.AgentAsTools;

/// <summary>
/// 可选的子 Agent 流式输出转发器。
/// 注册到 DI 后，AgentAsToolsExecutionStrategy 会对子 Agent 使用流式执行，
/// 并通过此接口实时转发每个 delta chunk。
/// 未注册时，子 Agent 使用非流式执行（原有行为）。
/// </summary>
[ExperimentalApi(Reason = "AgentAsTools 流式转发机制，API 可能变更")]
public interface IAgentStreamForwarder
{
    /// <summary>
    /// 将子 Agent 的流式 delta chunk 转发给调用方
    /// </summary>
    Task WriteAsync(string agentName, string delta, CancellationToken cancellationToken = default);
}
