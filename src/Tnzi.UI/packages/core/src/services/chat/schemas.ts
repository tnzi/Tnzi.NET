/**
 * Chat Module Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const messageTypeSchema = z.number().int().min(1).max(3);
export const messageStatusSchema = z.number().int().min(0).max(3);

// ============================================
// Attachment Schema
// ============================================

export const attachmentSchema = z.object({
  id: z.string(),
  fileName: z.string(),
  url: z.string().url(),
  size: z.number().int().nonnegative(),
  contentType: z.string(),
});

// ============================================
// Message Schemas
// ============================================

export const chatReplyDtoSchema: z.ZodTypeAny = z.object({
  id: z.string(),
  messageId: z.string(),
  parentId: z.string().nullable().optional(),
  content: z.string().min(1),
  senderId: z.string(),
  senderName: z.string().nullable().optional(),
  senderAvatar: z.string().url().nullable().optional(),
  isApproved: z.boolean(),
  approvedAt: z.union([z.date(), z.string()]).nullable().optional(),
  approvedBy: z.string().nullable().optional(),
  likeCount: z.number().int().nonnegative(),
  replies: z.array(z.lazy(() => chatReplyDtoSchema)),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const chatMessageDtoSchema = z.object({
  id: z.string(),
  title: z.string().min(1).max(200),
  content: z.string().min(1),
  messageType: messageTypeSchema,
  status: messageStatusSchema,
  senderId: z.string(),
  senderName: z.string().nullable().optional(),
  senderAvatar: z.string().url().nullable().optional(),
  newReplyCount: z.number().int().nonnegative(),
  isPinned: z.boolean(),
  canReply: z.boolean(),
  beginDate: z.union([z.date(), z.string()]).nullable().optional(),
  endDate: z.union([z.date(), z.string()]).nullable().optional(),
  scheduledAt: z.union([z.date(), z.string()]).nullable().optional(),
  sentAt: z.union([z.date(), z.string()]).nullable().optional(),
  recipientCount: z.number().int().nonnegative(),
  readCount: z.number().int().nonnegative(),
  tags: z.array(z.string()),
  attachments: z.array(attachmentSchema),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const chatMessageDetailDtoSchema = chatMessageDtoSchema.extend({
  replies: z.array(chatReplyDtoSchema),
  isRead: z.boolean(),
  readAt: z.union([z.date(), z.string()]).nullable().optional(),
});

// ============================================
// Request Schemas
// ============================================

export const createChatMessageSchema = z.object({
  title: z.string().min(1).max(200),
  content: z.string().min(1),
  messageType: messageTypeSchema,
  recipientIds: z.array(z.string()).optional(),
  recipientRoleIds: z.array(z.string()).optional(),
  recipientOrgIds: z.array(z.string()).optional(),
  canReply: z.boolean().optional(),
  beginDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  scheduledAt: z.union([z.date(), z.string()]).optional(),
  sendImmediately: z.boolean().optional(),
  tags: z.array(z.string().max(50)).optional(),
  attachmentIds: z.array(z.string()).optional(),
});

export const updateChatMessageSchema = z.object({
  title: z.string().min(1).max(200).optional(),
  content: z.string().min(1).optional(),
  messageType: messageTypeSchema.optional(),
  canReply: z.boolean().optional(),
  beginDate: z.union([z.date(), z.string()]).nullable().optional(),
  endDate: z.union([z.date(), z.string()]).nullable().optional(),
  scheduledAt: z.union([z.date(), z.string()]).nullable().optional(),
  tags: z.array(z.string().max(50)).optional(),
});

export const createChatReplySchema = z.object({
  messageId: z.string().min(1),
  parentId: z.string().optional(),
  content: z.string().min(1).max(5000),
});

export const chatMessageQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  messageType: messageTypeSchema.optional(),
  status: messageStatusSchema.optional(),
  senderId: z.string().optional(),
  isPinned: z.boolean().optional(),
  tag: z.string().optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  keyword: z.string().optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

// ============================================
// Inbox Schemas
// ============================================

export const messageReceiveDtoSchema = z.object({
  id: z.string(),
  messageId: z.string(),
  userId: z.string(),
  title: z.string(),
  content: z.string(),
  messageType: messageTypeSchema,
  senderId: z.string(),
  senderName: z.string().nullable().optional(),
  senderAvatar: z.string().url().nullable().optional(),
  isRead: z.boolean(),
  readAt: z.union([z.date(), z.string()]).nullable().optional(),
  isPinned: z.boolean(),
  hasReplies: z.boolean(),
  newReplyCount: z.number().int().nonnegative(),
  tags: z.array(z.string()),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const inboxQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  isRead: z.boolean().optional(),
  isPinned: z.boolean().optional(),
  messageType: messageTypeSchema.optional(),
  tag: z.string().optional(),
  senderId: z.string().optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

// ============================================
// Conversation Schemas
// ============================================

export const lastMessageSchema = z.object({
  id: z.string(),
  senderId: z.string(),
  senderName: z.string(),
  content: z.string(),
  sentAt: z.union([z.date(), z.string()]),
  isRead: z.boolean(),
});

export const conversationParticipantSchema = z.object({
  userId: z.string(),
  userName: z.string(),
  avatar: z.string().url().nullable().optional(),
  lastReadAt: z.union([z.date(), z.string()]).nullable().optional(),
  isOnline: z.boolean(),
});

export const conversationDtoSchema = z.object({
  id: z.string(),
  type: z.enum(['direct', 'group']),
  name: z.string().nullable().optional(),
  avatar: z.string().url().nullable().optional(),
  participants: z.array(conversationParticipantSchema),
  lastMessage: lastMessageSchema.nullable().optional(),
  unreadCount: z.number().int().nonnegative(),
  isPinned: z.boolean(),
  isMuted: z.boolean(),
  updatedAt: z.union([z.date(), z.string()]),
});
