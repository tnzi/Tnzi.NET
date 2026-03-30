namespace Tnzi.AI.Channels.Entities;

/// <summary>
/// IM 会话到 AI 线程的映射实体
/// </summary>
public class ChannelThreadMapping : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>适配器名称（telegram, feishu, dingtalk 等）</summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>IM 会话/群 ID</summary>
    public string ChatId { get; set; } = string.Empty;

    /// <summary>话题 ID（可选）</summary>
    public string? TopicId { get; set; }

    /// <summary>AI 线程 ID</summary>
    public Guid ThreadId { get; set; }

    /// <summary>创建此映射的 IM 用户 ID</summary>
    public string? ChannelUserId { get; set; }

    /// <summary>租户 ID</summary>
    public Guid? TenantId { get; set; }
}
