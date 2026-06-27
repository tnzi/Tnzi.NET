using Tnzi.Data;

namespace Tnzi.Chat.Dtos;

/// <summary>Chat 模块全局统计概览（管理端 dashboard）。</summary>
public class ChatStatisticsDto
{
    /// <summary>会话总数（不含已解散的软删群）。</summary>
    public int TotalConversations { get; set; }
    public int DirectConversations { get; set; }
    public int GroupConversations { get; set; }
    public int SystemConversations { get; set; }

    /// <summary>消息总数（不含已撤回的软删消息）。</summary>
    public int TotalMessages { get; set; }

    /// <summary>今日（UTC）新增消息数。</summary>
    public int MessagesToday { get; set; }

    /// <summary>活跃成员数（去重：仍在会话中的不同用户）。</summary>
    public int ActiveMembers { get; set; }

    /// <summary>当前在线用户数（有效在线，需 SignalR 已加载；否则为 0）。</summary>
    public int OnlineUsers { get; set; }
}

/// <summary>管理端会话分页查询条件。</summary>
public class AdminConversationQueryDto : PagedQueryDto
{
    /// <summary>按会话类型过滤（Direct/Group/System）。</summary>
    public ConversationType? Type { get; set; }

    /// <summary>关键字（匹配群名 Title，大小写不敏感）。</summary>
    public string? Keyword { get; set; }

    /// <summary>仅返回该用户参与（仍在会话中）的会话。</summary>
    public Guid? UserId { get; set; }
}

/// <summary>管理端会话列表项（分页）。</summary>
public class AdminConversationListItemDto
{
    public Guid Id { get; set; }
    public ConversationType Type { get; set; }

    /// <summary>群名；Direct/System 由管理端按类型展示派生标题。</summary>
    public string? Title { get; set; }

    public Guid? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public int MemberCount { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>管理端会话详情（成员 + 元数据）。</summary>
public class AdminConversationDetailDto
{
    public Guid Id { get; set; }
    public ConversationType Type { get; set; }
    public string? Title { get; set; }
    public string? Notice { get; set; }
    public Guid? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public string? DirectKey { get; set; }
    public int MemberCount { get; set; }
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreationTime { get; set; }
    public List<AdminConversationMemberDto> Members { get; set; } = new();
}

/// <summary>管理端会话成员视图。</summary>
public class AdminConversationMemberDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarFileId { get; set; }
    public MemberRole Role { get; set; }
    public string? Alias { get; set; }
    public int UnreadCount { get; set; }
    public DateTime? LastReadAt { get; set; }

    /// <summary>加入会话时间（成员行的创建时间）。</summary>
    public DateTime JoinedAt { get; set; }
}

/// <summary>管理端在线状态总览查询条件。</summary>
public class PresenceOverviewQueryDto
{
    /// <summary>按意图状态过滤（Online/Away/Busy/Invisible/Offline）。</summary>
    public UserPresenceStatus? Status { get; set; }

    /// <summary>仅返回当前有效在线的用户。</summary>
    public bool OnlineOnly { get; set; }
}

/// <summary>管理端在线状态总览（分布统计 + 用户明细）。</summary>
public class PresenceOverviewDto
{
    /// <summary>有 presence 记录的用户总数。</summary>
    public int Total { get; set; }

    /// <summary>有效在线用户数（有连接且意图非隐身/离线）。</summary>
    public int Online { get; set; }
    public int Away { get; set; }
    public int Busy { get; set; }

    /// <summary>有效离线用户数（无连接，或意图为隐身/离线）。</summary>
    public int Offline { get; set; }

    public List<PresenceUserDto> Users { get; set; } = new();
}

/// <summary>管理端在线用户明细。</summary>
public class PresenceUserDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarFileId { get; set; }

    /// <summary>用户设置的意图状态（隐身用户对管理员可见，区别于普通用户视角）。</summary>
    public UserPresenceStatus IntentStatus { get; set; }

    /// <summary>有效状态（综合连接情况；隐身/离线意图或无连接 → Offline）。</summary>
    public UserPresenceStatus EffectiveStatus { get; set; }

    /// <summary>是否有活跃连接（SignalR 未加载时恒为 false）。</summary>
    public bool HasConnection { get; set; }

    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastChangedAt { get; set; }
}

/// <summary>广播历史记录项。</summary>
public class BroadcastLogDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public BroadcastTargetType TargetType { get; set; }
    public string? TargetSummary { get; set; }
    public int RecipientCount { get; set; }
    public Guid? SenderId { get; set; }
    public string? SenderName { get; set; }
    public DateTime CreationTime { get; set; }
}
