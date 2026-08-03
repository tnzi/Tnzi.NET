using Tnzi.Data;

namespace Tnzi.Chat.Services;

/// <summary>
/// 系统级管理员的 Chat 维护服务：跨用户的全局会话/消息/在线状态查询与治理。
/// 区别于面向当前用户视角的 <see cref="IConversationService"/> / <see cref="IPresenceService"/>，
/// 本服务不做"是否会话成员"的门控，供后台管理使用。
/// </summary>
public interface IChatAdminService
{
    /// <summary>全局统计概览（会话/消息/成员/在线数）。</summary>
    Task<Result<ChatStatisticsDto>> GetStatisticsAsync();

    /// <summary>分页查询全部会话（按类型/关键字/参与用户过滤）。</summary>
    Task<Result<IPagedList<AdminConversationListItemDto>>> GetConversationsAsync(AdminConversationQueryDto query);

    /// <summary>会话详情（成员列表 + 元数据），不限当前用户是否为成员。</summary>
    Task<Result<AdminConversationDetailDto>> GetConversationDetailAsync(Guid conversationId);

    /// <summary>管理视角读取会话消息（游标分页，不受成员身份/清除历史水位限制）。</summary>
    Task<Result<MessageThreadDto>> GetConversationMessagesAsync(Guid conversationId, MessageThreadQueryDto query);

    /// <summary>删除会话（软删；通知在群成员会话已解散）。</summary>
    Task<Result> DeleteConversationAsync(Guid conversationId);

    /// <summary>强制撤回任意一条消息（软删），不限发送者。</summary>
    Task<Result> DeleteMessageAsync(Guid messageId);

    /// <summary>在线状态总览（有效在线分布 + 用户明细）。</summary>
    Task<Result<PresenceOverviewDto>> GetPresenceOverviewAsync(PresenceOverviewQueryDto query);

    /// <summary>广播历史记录（分页，按发送时间倒序）。</summary>
    Task<Result<IPagedList<BroadcastLogDto>>> GetBroadcastsAsync(PagedQueryDto query);
}
