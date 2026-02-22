/**
 * Chat Module Types - Internal messaging system
 * Aligned with Tnzi.NET backend Chat module
 */

import type { AuditedEntity, FullAuditedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';
import { MessageType, MessageStatus } from './metadata';

export { MessageType, MessageStatus };

// ============================================
// Message Types
// ============================================

/**
 * Message DTO
 */
export interface ChatMessageDto extends AuditedEntity<string> {
  title: string;
  content: string;
  messageType: MessageType;
  status: MessageStatus;
  senderId: string;
  senderName?: string | null;
  senderAvatar?: string | null;
  newReplyCount: number;
  isPinned: boolean;
  canReply: boolean;
  beginDate?: Date | string | null;
  endDate?: Date | string | null;
  scheduledAt?: Date | string | null;
  sentAt?: Date | string | null;
  recipientCount: number;
  readCount: number;
  tags: string[];
  attachments: AttachmentDto[];
}

/**
 * Message detail with replies
 */
export interface ChatMessageDetailDto extends ChatMessageDto {
  replies: ChatReplyDto[];
  isRead: boolean;
  readAt?: Date | string | null;
}

/**
 * Message reply DTO
 */
export interface ChatReplyDto extends AuditedEntity<string> {
  messageId: string;
  parentId?: string | null;
  content: string;
  senderId: string;
  senderName?: string | null;
  senderAvatar?: string | null;
  isApproved: boolean;
  approvedAt?: Date | string | null;
  approvedBy?: string | null;
  likeCount: number;
  replies: ChatReplyDto[];
}

/**
 * Message attachment
 */
export interface AttachmentDto {
  id: string;
  fileName: string;
  url: string;
  size: number;
  contentType: string;
}

// ============================================
// Request Types
// ============================================

/**
 * Create message request
 */
export interface CreateChatMessageDto {
  title: string;
  content: string;
  messageType: MessageType;
  recipientIds?: string[];
  recipientRoleIds?: string[];
  recipientOrgIds?: string[];
  canReply?: boolean;
  beginDate?: Date | string;
  endDate?: Date | string;
  scheduledAt?: Date | string;
  sendImmediately?: boolean;
  tags?: string[];
  attachmentIds?: string[];
}

/**
 * Update message request
 */
export interface UpdateChatMessageDto {
  title?: string;
  content?: string;
  messageType?: MessageType;
  canReply?: boolean;
  beginDate?: Date | string;
  endDate?: Date | string;
  scheduledAt?: Date | string;
  tags?: string[];
}

/**
 * Create reply request
 */
export interface CreateChatReplyDto {
  messageId: string;
  parentId?: string;
  content: string;
}

/**
 * Message list query
 */
export interface ChatMessageQueryDto extends SortedPagedQueryDto {
  messageType?: MessageType;
  status?: MessageStatus;
  senderId?: string;
  isPinned?: boolean;
  tag?: string;
  startDate?: Date | string;
  endDate?: Date | string;
  keyword?: string;
}

// ============================================
// Message Receive Types
// ============================================

/**
 * Message receive record (for inbox)
 */
export interface MessageReceiveDto extends FullAuditedEntity<string> {
  messageId: string;
  userId: string;
  title: string;
  content: string;
  messageType: MessageType;
  senderId: string;
  senderName?: string | null;
  senderAvatar?: string | null;
  isRead: boolean;
  readAt?: Date | string | null;
  isPinned: boolean;
  hasReplies: boolean;
  newReplyCount: number;
  tags: string[];
}

/**
 * Inbox query parameters
 */
export interface InboxQueryDto extends SortedPagedQueryDto {
  isRead?: boolean;
  isPinned?: boolean;
  messageType?: MessageType;
  tag?: string;
  senderId?: string;
  startDate?: Date | string;
  endDate?: Date | string;
}

// ============================================
// Conversation Types (if direct messaging)
// ============================================

/**
 * Conversation DTO
 */
export interface ConversationDto {
  id: string;
  type: 'direct' | 'group';
  name?: string | null;
  avatar?: string | null;
  participants: ConversationParticipantDto[];
  lastMessage?: LastMessageDto;
  unreadCount: number;
  isPinned: boolean;
  isMuted: boolean;
  updatedAt: Date | string;
}

/**
 * Conversation participant
 */
export interface ConversationParticipantDto {
  userId: string;
  userName: string;
  avatar?: string | null;
  lastReadAt?: Date | string | null;
  isOnline: boolean;
}

/**
 * Last message preview
 */
export interface LastMessageDto {
  id: string;
  senderId: string;
  senderName: string;
  content: string;
  sentAt: Date | string;
  isRead: boolean;
}

// ============================================
// Statistics Types
// ============================================

/**
 * Chat statistics
 */
export interface ChatStatisticsDto {
  totalMessages: number;
  totalReplies: number;
  unreadCount: number;
  byType: Record<MessageType, number>;
  byStatus: Record<MessageStatus, number>;
  recentActivity: DailyActivityDto[];
}

/**
 * Daily activity
 */
export interface DailyActivityDto {
  date: string;
  messages: number;
  replies: number;
  reads: number;
}
