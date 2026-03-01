namespace Tnzi.AI.A2A;

/// <summary>
/// A2A 客户端接口 — 用于与远程 Agent 通信
/// </summary>
[ExperimentalApi(Reason = "A2A protocol is in preview")]
public interface IA2AClient
{
    /// <summary>
    /// 发现远程 Agent 的能力（通过 .well-known/agent-card）
    /// </summary>
    Task<IAgentCard> DiscoverAsync(string endpoint, CancellationToken ct = default);

    /// <summary>
    /// 发送任务给远程 Agent
    /// </summary>
    Task<A2AResponse> SendTaskAsync(string endpoint, A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// 获取远程 Agent 上的任务状态
    /// </summary>
    Task<A2AResponse> GetTaskStatusAsync(string endpoint, string taskId, CancellationToken ct = default);
}

/// <summary>
/// A2A 任务请求
/// </summary>
public class A2ATaskRequest
{
    /// <summary>
    /// 任务 ID（默认自动生成）
    /// </summary>
    public string TaskId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 任务输入内容
    /// </summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// 任务参数
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

/// <summary>
/// A2A 任务响应
/// </summary>
public class A2AResponse
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 任务状态: "pending", "running", "completed", "failed"
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 任务输出（成功时）
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// 错误信息（失败时）
    /// </summary>
    public string? Error { get; set; }
}
