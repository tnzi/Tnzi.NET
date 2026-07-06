namespace Tnzi.Chat.Dtos;

/// <summary>会话列表项（左栏）。</summary>
public class ConversationListItemDto
{
    public Guid Id { get; set; }
    public ConversationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? AvatarFileId { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsMuted { get; set; }
    public int MemberCount { get; set; }
    public Guid? PeerUserId { get; set; }
    public UserPresenceStatus? PeerStatus { get; set; }
    public bool IsSticky { get; set; }
    public string? Remark { get; set; }

    /// <summary>
    /// 群聊头像拼合数据：群主恒第一，其余按入群顺序取前 N 个成员（N 由
    /// <c>ChatOptions.GroupAvatarMemberCount</c> 决定，1-9），前端按九宫格拼合渲染。
    /// 仅 Group 会话返回；Direct/System 为 null。
    /// </summary>
    public List<ChatContactDto>? MemberAvatars { get; set; }
}

public class ConversationMemberDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarFileId { get; set; }
    public MemberRole Role { get; set; }
    public UserPresenceStatus? Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? Alias { get; set; }
}

public class ConversationDto
{
    public Guid Id { get; set; }
    public ConversationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? AvatarFileId { get; set; }
    public Guid? OwnerId { get; set; }
    public int MemberCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public List<ConversationMemberDto> Members { get; set; } = new();
    public string? Notice { get; set; }
    public bool IsSticky { get; set; }
    public bool IsMuted { get; set; }
    public string? MyRemark { get; set; }
    public string? MyAlias { get; set; }
}

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderAvatarFileId { get; set; }
    public MessageContentType ContentType { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public DateTime SentAt { get; set; }
}

/// <summary>会话消息流（游标分页，向上翻历史）。</summary>
public class MessageThreadDto
{
    public List<ChatMessageDto> Messages { get; set; } = new();
    public bool HasMore { get; set; }
}

public class MessageThreadQueryDto
{
    /// <summary>取此消息 id 之前的更早消息（向上翻历史）；null=取最新一页。</summary>
    public Guid? Before { get; set; }
    public int Limit { get; set; } = 30;
}

public class SendMessageDto
{
    public MessageContentType ContentType { get; set; } = MessageContentType.Text;
    public string? Content { get; set; }
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
}

public class StartDirectDto
{
    // Guid is a value type; = null! does not compile — drop the initializer
    public Guid UserId { get; set; }
}

public class CreateGroupDto
{
    public string Title { get; set; } = null!;
    public List<Guid> MemberIds { get; set; } = null!;
}

public class RenameGroupDto
{
    public string Title { get; set; } = null!;
}

public class AddMembersDto
{
    public List<Guid> UserIds { get; set; } = null!;
}

public class MuteRequestDto
{
    public bool Muted { get; set; }
}

public class BroadcastDto
{
    public string Content { get; set; } = null!;
    public List<Guid>? RoleIds { get; set; }
    public List<Guid>? UserIds { get; set; }

    /// <summary>When true, deliver to every user (system-wide notification); RoleIds/UserIds are ignored.</summary>
    public bool All { get; set; }
}

public class ChatContactDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarFileId { get; set; }
}

public class UserPresenceDto
{
    public Guid UserId { get; set; }
    public UserPresenceStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public class SetPresenceDto
{
    public UserPresenceStatus Status { get; set; }
}

public class ChatContactProfileDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarFileId { get; set; }
    public UserPresenceStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }

    /// <summary>Contact email — shown on the profile card when available.</summary>
    public string? Email { get; set; }

    /// <summary>Contact phone number — shown on the profile card when available.</summary>
    public string? Phone { get; set; }

    /// <summary>Short personal signature / bio — shown on the profile card when available.</summary>
    public string? Bio { get; set; }
}

public class ConversationMemberSettingsDto
{
    public bool? IsMuted { get; set; }
    public bool? IsSticky { get; set; }

    /// <summary>隐藏会话（true=从列表移除；收到新消息时服务端自动取消隐藏）。</summary>
    public bool? IsHidden { get; set; }

    public string? Remark { get; set; }
    public string? Alias { get; set; }
}

public class UpdateNoticeDto
{
    public string? Notice { get; set; }
}

/// <summary>
/// 聊天客户端功能配置（供前端裁剪 UI 入口；写路径仍由服务端强制）。
/// </summary>
public class ChatClientConfigDto
{
    /// <summary>是否启用群聊（建群/加人入口）。</summary>
    public bool EnableGroups { get; set; }

    /// <summary>单个群的最大成员数（0 = 不限制）。</summary>
    public int MaxGroupMembers { get; set; }

    /// <summary>群头像拼合选取的成员数上限（1-9）。</summary>
    public int GroupAvatarMemberCount { get; set; }

    /// <summary>是否启用在线状态展示。</summary>
    public bool EnablePresence { get; set; }

    /// <summary>新消息提示音默认开关。</summary>
    public bool EnableMessageSound { get; set; }

    /// <summary>是否允许发送图片/文件消息。</summary>
    public bool EnableFileMessages { get; set; }
}
