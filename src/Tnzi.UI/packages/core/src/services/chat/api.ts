/**
 * Chat Module API - Internal messaging and direct conversations
 * Aligned with Tnzi.NET backend Chat controllers
 */

import type { HttpClient } from '../../http/http';
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
} from './types';

// Routes aligned with backend DefaultChatController / DefaultChatAdminController
const BASE = '/messages';
const ADMIN_BASE = '/admin/messages';

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
