namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// Gateway 统一控制面 — 处理入站请求、路由到 Agent、管理活跃会话
/// </summary>
[ExperimentalApi(Reason = "Gateway/Device/Workspace API under active development")]
public interface IGateway
{
    /// <summary>处理入站请求（非流式）</summary>
    Task<GatewayResponse> ProcessAsync(GatewayRequest request, CancellationToken ct = default);

    /// <summary>处理入站请求（流式）</summary>
    IAsyncEnumerable<GatewayStreamChunk> ProcessStreamingAsync(GatewayRequest request, CancellationToken ct = default);

    /// <summary>获取活跃会话列表（可按 agentId 过滤）</summary>
    Task<IReadOnlyList<GatewaySession>> GetSessionsAsync(string? agentId = null);

    /// <summary>获取指定会话</summary>
    Task<GatewaySession?> GetSessionAsync(string sessionKey);

    /// <summary>清理指定会话</summary>
    Task PruneSessionAsync(string sessionKey);
}
