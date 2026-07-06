/**
 * Chat Module API — system-level instant messaging (IM)
 * Aligned with Tnzi.NET backend Chat controllers
 */

import type { HttpClient } from '../../http/http';
import type {
  ConversationListItemDto, ConversationDto, ChatMessageDto, MessageThreadDto,
  SendMessageDto, CreateGroupDto, AddMembersDto, ChatContactDto, BroadcastDto,
  ConversationMemberSettingsDto, ChatContactProfileDto, UserPresenceDto, UserPresenceStatus,
  ChatClientConfigDto,
  ChatStatisticsDto, AdminConversationQueryDto, AdminConversationListItemDto,
  AdminConversationDetailDto, PresenceOverviewDto, PresenceOverviewQueryDto,
  BroadcastLogDto,
} from './types';
import type { PagedList } from '../../types/pagination';

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

    /** Per-user delete (wipe my history + hide from my list) - POST /conversations/{id}/delete-for-me */
    deleteForMe: (id: string) =>
      client.post<void>(`${CONV}/${id}/delete-for-me`),

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

    /** Deployment feature config - GET /chat/config */
    getConfig: () =>
      client.get<ChatClientConfigDto>('/chat/config'),
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

// ============================================
// Admin API (system maintenance) — /admin/chat/*
// ============================================

const ADMIN_CHAT = '/admin/chat';

/**
 * Chat Broadcast API (Admin) — send system notifications
 * Backend: DefaultChatAdminController [Route("admin/chat")]
 */
export function useChatBroadcastApi(client: HttpClient) {
  return {
    /** Broadcast message - POST /admin/chat/broadcast */
    broadcast: (data: BroadcastDto) =>
      client.post<number>(`${ADMIN_CHAT}/broadcast`, data),
  };
}

/**
 * Chat Admin API — global conversation / message / presence maintenance.
 * Backend: DefaultChatAdminController [Route("admin/chat")]
 */
export function useChatAdminApi(client: HttpClient) {
  return {
    /** Statistics overview - GET /admin/chat/statistics */
    getStatistics: () =>
      client.get<ChatStatisticsDto>(`${ADMIN_CHAT}/statistics`),

    /** Query all conversations (paged) - POST /admin/chat/conversations/query */
    queryConversations: (data: AdminConversationQueryDto) =>
      client.post<PagedList<AdminConversationListItemDto>>(`${ADMIN_CHAT}/conversations/query`, data),

    /** Conversation detail (members + metadata) - GET /admin/chat/conversations/{id} */
    getConversation: (id: string) =>
      client.get<AdminConversationDetailDto>(`${ADMIN_CHAT}/conversations/${id}`),

    /** Conversation messages (admin view) - GET /admin/chat/conversations/{id}/messages */
    getConversationMessages: (id: string, params: { before?: string; limit?: number }) =>
      client.get<MessageThreadDto>(`${ADMIN_CHAT}/conversations/${id}/messages`, { params }),

    /** Delete (dissolve) a conversation - DELETE /admin/chat/conversations/{id} */
    deleteConversation: (id: string) =>
      client.delete<void>(`${ADMIN_CHAT}/conversations/${id}`),

    /** Force-recall any message - DELETE /admin/chat/messages/{messageId} */
    deleteMessage: (messageId: string) =>
      client.delete<void>(`${ADMIN_CHAT}/messages/${messageId}`),

    /** Presence overview - GET /admin/chat/presence */
    getPresenceOverview: (params?: PresenceOverviewQueryDto) =>
      client.get<PresenceOverviewDto>(`${ADMIN_CHAT}/presence`, { params }),

    /** Broadcast history (paged) - GET /admin/chat/broadcasts */
    getBroadcasts: (params?: { pageIndex?: number; pageSize?: number }) =>
      client.get<PagedList<BroadcastLogDto>>(`${ADMIN_CHAT}/broadcasts`, { params }),
  };
}
