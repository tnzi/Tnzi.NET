/**
 * Chat Module Types - Internal messaging system
 * Aligned with Tnzi.NET backend Chat module (Tnzi.Chat/Dtos/ChatDtos.cs)
 */

import type { SortedPagedQueryDto } from '../../types/pagination';
import { MessageType } from './metadata';

export { MessageType };

// ============================================
// Message Types
// ============================================

/**
 * Message list item DTO (without replies and full content)
 * Backend: MessageListItemDto
 */
export interface MessageListItemDto {
  id: string;
  title: string;
  messageType: MessageType;
  senderId: string;
  senderName?: string | null;
  canReply: boolean;
  creationTime: string;
  isRead: boolean;
  replyCount: number;
  isImportant: boolean;
}

/**
 * Message detail DTO (with replies and full content)
 * Backend: MessageDto
 */
export interface MessageDto {
  id: string;
  title: string;
  content: string;
  messageType: MessageType;
  senderId: string;
  senderName?: string | null;
  isSent: boolean;
  canReply: boolean;
  beginDate?: string | null;
  endDate?: string | null;
  creationTime: string;
  isRead: boolean;
  readTime?: string | null;
  replyCount: number;
  isImportant: boolean;
  replies: MessageReplyDto[];
}

/**
 * Message reply DTO (tree structure)
 * Backend: MessageReplyDto
 */
export interface MessageReplyDto {
  id: string;
  content: string;
  userId: string;
  userName?: string | null;
  belongMessageId: string;
  parentReplyId?: string | null;
  creationTime: string;
  replies: MessageReplyDto[];
}

// ============================================
// Request Types
// ============================================

/**
 * Create message request
 * Backend: CreateMessageDto
 */
export interface CreateMessageDto {
  title: string;
  content: string;
  messageType: MessageType;
  roleIds?: string[];
  recipientIds?: string[];
  canReply?: boolean;
  isImportant?: boolean;
  beginDate?: string;
  endDate?: string;
}

/**
 * Update message request (only sender can update)
 * Backend: UpdateMessageDto
 */
export interface UpdateMessageDto {
  title?: string;
  content?: string;
  canReply?: boolean;
  isImportant?: boolean;
  beginDate?: string;
  endDate?: string;
}

/**
 * Create reply request
 * Backend: CreateMessageReplyDto
 */
export interface CreateMessageReplyDto {
  messageId: string;
  content: string;
  parentReplyId?: string;
}

/**
 * Message query (user inbox)
 * Backend: MessageQueryDto extends PagedQueryDto
 */
export interface MessageQueryDto extends SortedPagedQueryDto {
  messageType?: MessageType;
  isRead?: boolean;
  isImportant?: boolean;
  keyword?: string;
  startDate?: string;
  endDate?: string;
}

/**
 * Admin message query
 * Backend: AdminMessageQueryDto extends PagedQueryDto
 */
export interface AdminMessageQueryDto extends SortedPagedQueryDto {
  messageType?: MessageType;
  isSent?: boolean;
  senderId?: string;
  keyword?: string;
  startDate?: string;
  endDate?: string;
}

// ============================================
// Statistics Types
// ============================================

/**
 * Chat statistics
 * Backend: ChatStatisticsDto
 */
export interface ChatStatisticsDto {
  totalMessages: number;
  sentMessages: number;
  draftMessages: number;
  publicMessages: number;
  privateMessages: number;
  totalReplies: number;
  activeSenders: number;
  importantMessages: number;
}

// ============================================
// Chat Session Types (admin-curated groupings; 2026-04-14)
// ============================================

/**
 * ChatSession lifecycle status — aligned with backend
 * <c>Tnzi.Chat.Entities.ChatSessionStatus</c>.
 */
export enum ChatSessionStatus {
  Active = 1,
  Archived = 2,
}

/**
 * Chat session detail DTO.
 * Backend: ChatSessionDto
 */
export interface ChatSessionDto {
  id: string;
  title: string;
  description?: string | null;
  status: ChatSessionStatus;
  participants: string[];
  messageCount: number;
  lastMessageAt?: string | null;
  creationTime: string;
  lastModificationTime?: string | null;
}

/**
 * Chat session list item DTO (paging-optimised).
 * Backend: ChatSessionListItemDto
 */
export interface ChatSessionListItemDto {
  id: string;
  title: string;
  status: ChatSessionStatus;
  participants: string[];
  messageCount: number;
  lastMessageAt?: string | null;
  creationTime: string;
}

/**
 * Create chat session request.
 * Backend: CreateChatSessionDto
 */
export interface CreateChatSessionDto {
  title: string;
  description?: string | null;
  status?: ChatSessionStatus;
  participants?: string[];
}

/**
 * Update chat session request.
 * Backend: UpdateChatSessionDto
 */
export interface UpdateChatSessionDto {
  title: string;
  description?: string | null;
  status?: ChatSessionStatus;
  participants?: string[];
}

/**
 * Chat session paged query.
 * Backend: ChatSessionQueryDto
 */
export interface ChatSessionQueryDto extends SortedPagedQueryDto {
  status?: ChatSessionStatus;
  keyword?: string;
  participantId?: string;
}
