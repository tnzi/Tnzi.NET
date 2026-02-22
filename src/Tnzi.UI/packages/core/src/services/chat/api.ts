/**
 * Chat Module API - Internal messaging and direct conversations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  ChatMessageDto,
  ChatReplyDto,
  CreateChatMessageDto,
  CreateChatReplyDto,
  ChatMessageQueryDto,
} from './types';

// Routes aligned with backend ChatController implementation
const BASE = '/messages'; // Common base for chat messages

/**
 * Chat Message API (User)
 */
export function useChatApi(client: HttpClient) {
  return {
    /** Query my messages */
    query: (data: ChatMessageQueryDto) =>
      client.post<PagedList<ChatMessageDto>>(`${BASE}/query`, data),

    /** Get message by ID */
    getById: (id: string) =>
      client.get<ChatMessageDto>(`${BASE}/${id}`),

    /** Send message */
    send: (data: CreateChatMessageDto) =>
      client.post<ChatMessageDto>(BASE, data),

    /** Reply to message */
    reply: (data: CreateChatReplyDto) =>
      client.post<ChatReplyDto>(`${BASE}/reply`, data),

    /** Get message replies */
    getReplies: (id: string) =>
      client.get<ChatReplyDto[]>(`${BASE}/${id}/replies`),

    /** Mark as read */
    markAsRead: (id: string) =>
      client.post<void>(`${BASE}/${id}/mark-read`),

    /** Get unread count */
    getUnreadCount: () =>
      client.get<number>(`${BASE}/unread-count`),

    /** Delete my message */
    delete: (id: string) =>
      client.delete<void>(`${BASE}/${id}`),
  };
}
