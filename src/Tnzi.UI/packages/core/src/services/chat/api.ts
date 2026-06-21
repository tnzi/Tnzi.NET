/**
 * Chat Module API - Internal messaging and direct conversations
 * Aligned with Tnzi.NET backend Chat controllers
 */

import type { HttpClient } from '../../http/http';
import type {
  ConversationListItemDto, ConversationDto, ChatMessageDto, MessageThreadDto,
  SendMessageDto, CreateGroupDto, AddMembersDto, ChatContactDto, BroadcastDto,
  ConversationMemberSettingsDto, ChatContactProfileDto, UserPresenceDto, UserPresenceStatus,
} from './types';
import type { PagedList } from '../../types/pagination';
import type {
  MessageDto,
  MessageListItemDto,
  MessageReplyDto,
  CreateMessageDto,
  UpdateMessageDto,
  CreateMessageReplyDto,
  MessageQueryDto,
  AdminMessageQueryDto,
  ChatStatisticsDto,
  ChatSessionDto,
  ChatSessionListItemDto,
  CreateChatSessionDto,
  UpdateChatSessionDto,
  ChatSessionQueryDto,
} from './types';

// Routes aligned with backend DefaultChatController / DefaultChatAdminController
const BASE = '/messages';
const ADMIN_BASE = '/admin/messages';
const ADMIN_SESSION_BASE = '/admin/chat-sessions';

/**
 * Chat Message API (User)
 * Backend: DefaultChatController [Route("messages")]
 */
export function useChatApi(client: HttpClient) {
  return {
    /** Send message - POST /messages */
    send: (data: CreateMessageDto) =>
      client.post<MessageDto>(BASE, data),

    /** Query inbox - POST /messages/query */
    query: (data: MessageQueryDto) =>
      client.post<PagedList<MessageListItemDto>>(`${BASE}/query`, data),

    /** Get message by ID - GET /messages/{id} */
    getById: (id: string) =>
      client.get<MessageDto>(`${BASE}/${id}`),

    /** Update message (sender only) - PUT /messages/{id} */
    update: (id: string, data: UpdateMessageDto) =>
      client.put<MessageDto>(`${BASE}/${id}`, data),

    /** Delete message (sender only, soft delete) - DELETE /messages/{id} */
    delete: (id: string) =>
      client.delete<void>(`${BASE}/${id}`),

    /** Mark as read - POST /messages/{id}/read */
    markAsRead: (id: string) =>
      client.post<void>(`${BASE}/${id}/read`),

    /** Mark all as read - POST /messages/read-all */
    markAllAsRead: () =>
      client.post<number>(`${BASE}/read-all`),

    /** Get unread count - GET /messages/unread-count */
    getUnreadCount: () =>
      client.get<number>(`${BASE}/unread-count`),

    /** Batch delete receives - POST /messages/batch-delete */
    batchDelete: (messageIds: string[]) =>
      client.post<number>(`${BASE}/batch-delete`, messageIds),

    /** Batch mark as read - POST /messages/batch-read */
    batchMarkAsRead: (messageIds: string[]) =>
      client.post<number>(`${BASE}/batch-read`, messageIds),

    /** Create reply - POST /messages/replies */
    createReply: (data: CreateMessageReplyDto) =>
      client.post<MessageReplyDto>(`${BASE}/replies`, data),

    /** Get message replies (tree) - GET /messages/{messageId}/replies */
    getReplies: (messageId: string) =>
      client.get<MessageReplyDto[]>(`${BASE}/${messageId}/replies`),

    /** Delete reply (author only, soft delete) - DELETE /messages/replies/{replyId} */
    deleteReply: (replyId: string) =>
      client.delete<void>(`${BASE}/replies/${replyId}`),
  };
}

/**
 * Chat Admin API
 * Backend: DefaultChatAdminController [Route("admin/messages")]
 */
export function useChatAdminApi(client: HttpClient) {
  return {
    /** Query all messages - POST /admin/messages/query */
    query: (data: AdminMessageQueryDto) =>
      client.post<PagedList<MessageListItemDto>>(`${ADMIN_BASE}/query`, data),

    /** Delete message - DELETE /admin/messages/{id} */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_BASE}/${id}`),

    /** Batch delete messages - POST /admin/messages/batch-delete */
    batchDelete: (ids: string[]) =>
      client.post<number>(`${ADMIN_BASE}/batch-delete`, ids),

    /** Get statistics - GET /admin/messages/statistics */
    getStatistics: (params?: { startDate?: string; endDate?: string }) =>
      client.get<ChatStatisticsDto>(`${ADMIN_BASE}/statistics`, { params }),
  };
}

/**
 * Chat Session Admin API
 * Backend: DefaultChatSessionAdminController [Route("admin/chat-sessions")]
 */
export function useAdminChatSessionApi(client: HttpClient) {
  return {
    /** Get paged list - GET /admin/chat-sessions */
    getList: (params?: ChatSessionQueryDto) =>
      client.get<PagedList<ChatSessionListItemDto>>(ADMIN_SESSION_BASE, { params }),

    /** Get by id - GET /admin/chat-sessions/{id} */
    getById: (id: string) =>
      client.get<ChatSessionDto>(`${ADMIN_SESSION_BASE}/${id}`),

    /** Create - POST /admin/chat-sessions */
    create: (data: CreateChatSessionDto) =>
      client.post<ChatSessionDto>(ADMIN_SESSION_BASE, data),

    /** Update - PUT /admin/chat-sessions/{id} */
    update: (id: string, data: UpdateChatSessionDto) =>
      client.put<ChatSessionDto>(`${ADMIN_SESSION_BASE}/${id}`, data),

    /** Delete - DELETE /admin/chat-sessions/{id} */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_SESSION_BASE}/${id}`),

    /** Batch delete - DELETE /admin/chat-sessions/batch */
    deleteBatch: (ids: string[]) =>
      client.delete<number>(`${ADMIN_SESSION_BASE}/batch`, { body: ids }),
  };
}

// ============================================
// IM (Instant Messaging) API — /conversations/*
// ============================================

const CONV = '/conversations';

/**
 * Chat IM API (User) — real-time instant messaging conversations
 * Backend: DefaultConversationController [Route("conversations")]
 */
export function useChatImApi(client: HttpClient) {
  return {
    /** List conversations - GET /conversations */
    getConversations: () =>
      client.get<ConversationListItemDto[]>(CONV),

    /** Unread count - GET /conversations/unread-count */
    getUnreadCount: () =>
      client.get<number>(`${CONV}/unread-count`),

    /** Get or create direct conversation - POST /conversations/direct */
    getOrCreateDirect: (userId: string) =>
      client.post<ConversationDto>(`${CONV}/direct`, { userId }),

    /** Get conversation detail - GET /conversations/{id} */
    getConversation: (id: string) =>
      client.get<ConversationDto>(`${CONV}/${id}`),

    /** Get messages with pagination - GET /conversations/{id}/messages */
    getMessages: (id: string, params: { before?: string; limit?: number }) =>
      client.get<MessageThreadDto>(`${CONV}/${id}/messages`, { params }),

    /** Send message - POST /conversations/{id}/messages */
    sendMessage: (id: string, data: SendMessageDto) =>
      client.post<ChatMessageDto>(`${CONV}/${id}/messages`, data),

    /** Mark conversation as read - POST /conversations/{id}/read */
    markRead: (id: string) =>
      client.post<void>(`${CONV}/${id}/read`),

    /** Mute/unmute conversation - POST /conversations/{id}/mute */
    mute: (id: string, muted: boolean) =>
      client.post<void>(`${CONV}/${id}/mute`, { muted }),

    /** Delete a message - DELETE /conversations/messages/{messageId} */
    deleteMessage: (messageId: string) =>
      client.delete<void>(`${CONV}/messages/${messageId}`),

    /** Create group conversation - POST /conversations/group */
    createGroup: (data: CreateGroupDto) =>
      client.post<ConversationDto>(`${CONV}/group`, data),

    /** Add members to group - POST /conversations/{id}/members */
    addMembers: (id: string, userIds: string[]) =>
      client.post<void>(`${CONV}/${id}/members`, { userIds } as AddMembersDto),

    /** Remove member from group - DELETE /conversations/{id}/members/{userId} */
    removeMember: (id: string, userId: string) =>
      client.delete<void>(`${CONV}/${id}/members/${userId}`),

    /** Rename group - PUT /conversations/{id} */
    renameGroup: (id: string, title: string) =>
      client.put<void>(`${CONV}/${id}`, { title }),

    /** Dissolve group - DELETE /conversations/{id} */
    dissolveGroup: (id: string) =>
      client.delete<void>(`${CONV}/${id}`),

    /** Leave group - POST /conversations/{id}/leave */
    leaveGroup: (id: string) =>
      client.post<void>(`${CONV}/${id}/leave`),

    /** Update per-member settings - PUT /conversations/{id}/member-settings */
    updateMemberSettings: (id: string, data: ConversationMemberSettingsDto) =>
      client.put<void>(`${CONV}/${id}/member-settings`, data),

    /** Clear my chat history - POST /conversations/{id}/clear */
    clearHistory: (id: string) =>
      client.post<void>(`${CONV}/${id}/clear`),

    /** Search messages in a conversation - GET /conversations/{id}/messages/search */
    searchMessages: (id: string, params: { keyword: string; before?: string; limit?: number }) =>
      client.get<MessageThreadDto>(`${CONV}/${id}/messages/search`, { params }),

    /** Update group notice - PUT /conversations/{id}/notice */
    updateNotice: (id: string, notice: string | null) =>
      client.put<void>(`${CONV}/${id}/notice`, { notice }),

    /** Get a contact's profile card - GET /chat/contacts/{userId}/profile */
    getContactProfile: (userId: string) =>
      client.get<ChatContactProfileDto>(`/chat/contacts/${userId}/profile`),

    /** Search contacts - GET /chat/contacts/search */
    searchContacts: (keyword: string) =>
      client.get<ChatContactDto[]>(`/chat/contacts/search`, { params: { keyword } }),
  };
}

/**
 * Presence API (User) — online status (manual + auto)
 * Backend: DefaultPresenceController [Route("presence")]
 */
export function usePresenceApi(client: HttpClient) {
  return {
    /** Set my manual status - PUT /presence */
    setStatus: (status: UserPresenceStatus) =>
      client.put<void>('/presence', { status }),

    /** Get my (manual) status - GET /presence/me */
    getMyStatus: () =>
      client.get<UserPresenceStatus>('/presence/me'),

    /** Resolve effective status for users - GET /presence?userIds=... */
    getPresence: (userIds: string[]) =>
      client.get<UserPresenceDto[]>('/presence', { params: { userIds } }),
  };
}

/**
 * Chat Broadcast API (Admin) — send broadcast messages
 * Backend: DefaultChatAdminController [Route("admin/chat/broadcast")]
 */
export function useChatBroadcastApi(client: HttpClient) {
  return {
    /** Broadcast message - POST /admin/chat/broadcast */
    broadcast: (data: BroadcastDto) =>
      client.post<number>('/admin/chat/broadcast', data),
  };
}
