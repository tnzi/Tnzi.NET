namespace Tnzi.AI.A2A;

/// <summary>
/// A2A 服务端接口 — 接收并处理来自远程 Agent 的任务
/// </summary>
[ExperimentalApi(Reason = "A2A protocol is in preview")]
public interface IA2AServer
{
    /// <summary>
    /// 获取当前 Agent 的名片信息
    /// </summary>
    IAgentCard GetAgentCard();

    /// <summary>
    /// 处理接收到的任务
    /// </summary>
    Task<A2AResponse> HandleTaskAsync(A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// 获取任务状态
    /// </summary>
    Task<A2AResponse> GetTaskStatusAsync(string taskId, CancellationToken ct = default);
}
