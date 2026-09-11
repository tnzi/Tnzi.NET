namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// Gateway 统一控制面 - 处理入站请求、路由到 Agent、管理活跃会话
/// </summary>
[ExperimentalApi(Reason = "Gateway API under active development")]
public interface IGateway
{
    /// <summary>处理入站请求（非流式）</summary>
    Task<GatewayResponse> ProcessAsync(GatewayRequest request, CancellationToken ct = default);

    /// <summary>处理入站请求（流式）</summary>
    IAsyncEnumerable<GatewayStreamChunk> ProcessStreamingAsync(GatewayRequest request, CancellationToken ct = default);

    /// <summary>
    /// 获取<b>全部</b>活跃会话（可按 agentId 过滤）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>无归属过滤</b>：返回每个 peer 的会话，含其 <see cref="GatewaySession.PeerId"/>
    /// （已认证渠道下就是用户 id）。仅供管理端与服务端内部使用，<b>绝不能</b>由客户端输入直接驱动。
    /// 客户端要列自己的会话走 <see cref="GetPeerSessionsAsync"/>。
    /// </remarks>
    Task<IReadOnlyList<GatewaySession>> GetSessionsAsync(string? agentId = null);

    /// <summary>
    /// 获取指定会话。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>无归属过滤</b>，理由同 <see cref="GetSessionsAsync"/>。会话键是可推导的
    /// （<c>agent:{agentId}:peer:{userId}</c> 之类），因此按键直取等于按用户 id 直取。
    /// 客户端路径走 <see cref="GetPeerSessionAsync"/>。
    /// </remarks>
    Task<GatewaySession?> GetSessionAsync(string sessionKey);

    /// <summary>
    /// 清理指定会话。
    /// </summary>
    /// <remarks>⚠️ <b>无归属过滤</b>，理由同 <see cref="GetSessionsAsync"/>；客户端路径走 <see cref="PrunePeerSessionAsync"/>。</remarks>
    Task PruneSessionAsync(string sessionKey);

    /// <summary>
    /// 列出<b>某个 peer 自己的</b>活跃会话（可按 agentId 过滤）。
    /// </summary>
    /// <remarks>
    /// ★ 与不带 peer 的重载分成两个方法而不是加一个可空参数：可空参数的默认值是"不过滤"，
    /// 于是任何一个忘了传它的客户端路径都会静默地拿到全量。分成两个方法后，拿到全量必须
    /// 显式调用另一个名字。
    /// </remarks>
    /// <param name="peerId">调用者的 peer 标识（已认证连接 = 用户 id；匿名连接 = 匿名命名空间下的标识）。</param>
    /// <param name="agentId">可选的 agent 过滤。</param>
    Task<IReadOnlyList<GatewaySession>> GetPeerSessionsAsync(string peerId, string? agentId = null);

    /// <summary>
    /// 取<b>某个 peer 自己的</b>会话；会话不存在或不属于该 peer 时一律返回 null
    /// （两种情况必须给同一个答案，否则按键探测就能确认某个会话存在）。
    /// </summary>
    Task<GatewaySession?> GetPeerSessionAsync(string peerId, string sessionKey);

    /// <summary>
    /// 清理<b>某个 peer 自己的</b>会话。
    /// </summary>
    /// <returns>是否真的清理了。不属于该 peer 或本就不存在时返回 false（同一个答案）。</returns>
    Task<bool> PrunePeerSessionAsync(string peerId, string sessionKey);
}
