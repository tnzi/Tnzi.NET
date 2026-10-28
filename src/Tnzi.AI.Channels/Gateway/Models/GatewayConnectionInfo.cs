
namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// Gateway 连接信息 - 描述一个 WebSocket 连接
/// </summary>
public class GatewayConnectionInfo
{
    /// <summary>连接 ID</summary>
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; init; } = string.Empty;

    /// <summary>用户 ID</summary>
    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    /// <summary>客户端类型（当前 WebSocket 入口固定为 "websocket"；"device" 类型已随 2026-06-10 服务端重定位移除，字段保留给未来客户端类型）</summary>
    [JsonPropertyName("clientType")]
    public string ClientType { get; init; } = string.Empty;

    /// <summary>连接建立时间</summary>
    [JsonPropertyName("connectedAt")]
    public DateTimeOffset ConnectedAt { get; init; }

    /// <summary>
    /// 连接所属租户（建立连接时由服务端从已认证身份解析；匿名 / 单租户为 null）。
    /// </summary>
    /// <remarks>
    /// 只供服务端按租户筛选（管理端诊断端点），不出现在线缆上：在线连接存在进程内单例里，
    /// 全局租户过滤器管不到它，不记下租户就无从筛起。
    /// </remarks>
    [JsonIgnore]
    public Guid? TenantId { get; init; }
}

/// <summary>
/// 在线状态变更事件
/// </summary>
public record PresenceChangedEvent(
    string ConnectionId,
    string? UserId,
    string ClientType,
    bool IsOnline,
    DateTimeOffset Timestamp);
