
namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// Gateway 会话信息 DTO - 描述一个活跃的 Gateway 会话
/// </summary>
public class GatewaySession
{
    /// <summary>会话唯一键</summary>
    [JsonPropertyName("sessionKey")]
    public string SessionKey { get; init; } = string.Empty;

    /// <summary>绑定的 Agent ID</summary>
    [JsonPropertyName("agentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AgentId { get; init; }

    /// <summary>关联的线程 ID</summary>
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ThreadId { get; init; }

    /// <summary>频道名称</summary>
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    /// <summary>Peer ID（用户/群组标识）</summary>
    [JsonPropertyName("peerId")]
    public string PeerId { get; init; } = string.Empty;

    /// <summary>
    /// 发出这条会话最近一条消息的调用者（<see cref="GatewayRequest.UserId"/>，服务端解析：已认证连接 = 用户 id，
    /// 匿名连接 = 匿名命名空间下的连接 id）。<c>GetPeerSessionsAsync</c> 等按它判归属。
    /// </summary>
    /// <remarks>
    /// 不用 <see cref="PeerId"/> 判归属：匿名客户端可以自选 chatId（重连后接回自己的话题），<see cref="PeerId"/>
    /// 记的就是那个自选值，而调用者的 peer 标识是连接 id，两者对不上，客户端于是永远列不出自己的会话。
    /// 只供服务端使用，不出现在线缆上。
    /// </remarks>
    [JsonIgnore]
    public string? OwnerPeerId { get; init; }

    /// <summary>最后活跃时间</summary>
    [JsonPropertyName("lastActivityAt")]
    public DateTimeOffset LastActivityAt { get; init; }

    /// <summary>
    /// 会话所属租户（取自 <see cref="GatewayRequest.TenantId"/>，服务端解析；null = 单租户 / 部署级）。
    /// </summary>
    /// <remarks>只供服务端按租户筛选，不出现在线缆上（理由同 <see cref="GatewayConnectionInfo.TenantId"/>）。</remarks>
    [JsonIgnore]
    public Guid? TenantId { get; init; }
}
