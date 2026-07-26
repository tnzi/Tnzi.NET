using System.Text.Json.Serialization;

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

    /// <summary>最后活跃时间</summary>
    [JsonPropertyName("lastActivityAt")]
    public DateTimeOffset LastActivityAt { get; init; }
}
