namespace Tnzi.Chat.Services;

/// <summary>
/// 「你在这个会话里，就看得见这个会话里的文件」。
///
/// 为什么需要它：Storage 自己只认识「文件的创建者」和「持 <c>storage.file.view</c> 的
/// 管理员」。可是聊天图片的**接收方**两样都不是——他既没上传那张图，也不该为了看一张
/// 图而拿到整个文件库的管理权限。发送方反倒因为是创建者而天然放行，于是同一张图
/// 在发的人那里能看、在收的人那里 404，这显然不是会话该有的样子。
///
/// 判据落在成员关系上，与 <c>IConversationService</c> 读消息用的是同一条：仍在群里
/// （<c>RemovedAt == null</c>）就能读。**被移出的人读不到**——这一点刻意与消息历史保持
/// 一致，而不是"曾经看得见就永远看得见"。
///
/// 覆盖两类引用：
/// <list type="bullet">
/// <item><c>ChatMessage.FileId</c> —— 图片 / 文件消息的正文</item>
/// <item><c>Conversation.AvatarFileId</c> —— 群头像。该字段已声明 <c>Public = true</c>
/// （头像本就要以匿名 <c>&lt;img&gt;</c> 渲染），这里一并覆盖是为了兜住"消费方把它改成
/// 非公开"或历史数据未回填的情形。</item>
/// </list>
/// </summary>
public class ChatFileReferenceAccessResolver : IFileReferenceAccessResolver
{
    private readonly ICurrentUser _currentUser;
    private readonly IRepository<ChatMessage, Guid> _messages;
    private readonly IRepository<ConversationMember, Guid> _members;

    public ChatFileReferenceAccessResolver(
        ICurrentUser currentUser,
        IRepository<ChatMessage, Guid> messages,
        IRepository<ConversationMember, Guid> members)
    {
        _currentUser = Check.NotNull(currentUser);
        _messages = Check.NotNull(messages);
        _members = Check.NotNull(members);
    }

    public bool CanHandle(string entityType)
        => entityType == nameof(ChatMessage) || entityType == nameof(Conversation);

    public async Task<bool> CanReadAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.Id;
        if (userId is null || userId == Guid.Empty)
            return false;

        var conversationId = await ResolveConversationIdAsync(reference, cancellationToken);
        if (conversationId is null)
            return false;

        return await _members.AnyAsync(
            m => m.ConversationId == conversationId.Value
                 && m.UserId == userId.Value
                 && m.RemovedAt == null,
            cancellationToken);
    }

    /// <summary>
    /// 引用指向消息时要多查一跳拿会话 id；指向会话时 EntityId 本身就是会话 id。
    /// 撤回的消息（软删）由全局过滤器挡掉，读不到会话 id 即不放行——撤回之后那张图
    /// 就不该再渲染出来。
    /// </summary>
    private async Task<Guid?> ResolveConversationIdAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken)
    {
        if (reference.EntityType == nameof(Conversation))
            return reference.EntityId;

        var message = await _messages.FindAsync(m => m.Id == reference.EntityId, cancellationToken);
        return message?.ConversationId;
    }
}
