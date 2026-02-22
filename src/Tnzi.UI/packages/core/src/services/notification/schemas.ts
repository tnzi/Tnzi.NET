/**
 * Notification Module Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const notificationTypeSchema = z.number().int().min(1).max(4);
export const notificationStatusSchema = z.number().int().min(0).max(4);
export const notificationPrioritySchema = z.number().int().min(0).max(3);
export const recipientTypeSchema = z.number().int().min(1).max(6);

// ============================================
// Message Schemas
// ============================================

export const fileInfoSchema = z.object({
  id: z.string(),
  name: z.string(),
  url: z.string().url(),
  size: z.number().int().nonnegative(),
  contentType: z.string(),
});

export const messageRecipientDtoSchema = z.object({
  id: z.string(),
  messageId: z.string(),
  recipientType: recipientTypeSchema,
  recipientId: z.string().nullable().optional(),
  recipientName: z.string().nullable().optional(),
  recipientEmail: z.string().email().nullable().optional(),
  recipientPhone: z.string().nullable().optional(),
  status: notificationStatusSchema,
  sentAt: z.union([z.date(), z.string()]).nullable().optional(),
  readAt: z.union([z.date(), z.string()]).nullable().optional(),
  errorMessage: z.string().nullable().optional(),
});

export const messageDtoSchema = z.object({
  id: z.string(),
  type: notificationTypeSchema,
  subject: z.string().min(1),
  content: z.string().min(1),
  isHtml: z.boolean(),
  category: z.string().nullable().optional(),
  priority: notificationPrioritySchema,
  status: notificationStatusSchema,
  senderId: z.string().nullable().optional(),
  senderName: z.string().nullable().optional(),
  templateId: z.string().nullable().optional(),
  templateName: z.string().nullable().optional(),
  layoutId: z.string().nullable().optional(),
  layoutName: z.string().nullable().optional(),
  maxRetryCount: z.number().int().nonnegative(),
  retryCount: z.number().int().nonnegative(),
  lastError: z.string().nullable().optional(),
  scheduledAt: z.union([z.date(), z.string()]).nullable().optional(),
  sentAt: z.union([z.date(), z.string()]).nullable().optional(),
  recipientCount: z.number().int().nonnegative(),
  attachments: z.array(fileInfoSchema),
  recipients: z.array(messageRecipientDtoSchema),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

// ============================================
// Request Schemas
// ============================================

export const recipientInfoSchema = z.object({
  type: recipientTypeSchema,
  id: z.string().optional(),
  email: z.string().email().optional(),
  phone: z.string().optional(),
  name: z.string().optional(),
});

export const createNotificationSchema = z.object({
  type: notificationTypeSchema,
  subject: z.string().min(1).max(200),
  content: z.string().min(1),
  isHtml: z.boolean().optional(),
  recipients: z.array(recipientInfoSchema).min(1),
  attachments: z.array(z.string()).optional(),
  sendImmediately: z.boolean().optional(),
  scheduledAt: z.union([z.date(), z.string()]).optional(),
  maxRetryCount: z.number().int().min(0).max(10).optional(),
  templateName: z.string().optional(),
  layoutName: z.string().optional(),
  templateVariables: z.record(z.unknown()).optional(),
  category: z.string().max(50).optional(),
  priority: notificationPrioritySchema.optional(),
  senderId: z.string().optional(),
});

export const updateNotificationSchema = z.object({
  subject: z.string().min(1).max(200).optional(),
  content: z.string().min(1).optional(),
  isHtml: z.boolean().optional(),
  scheduledAt: z.union([z.date(), z.string()]).nullable().optional(),
  priority: notificationPrioritySchema.optional(),
  category: z.string().max(50).nullable().optional(),
});

export const messageListQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  type: notificationTypeSchema.optional(),
  status: notificationStatusSchema.optional(),
  priority: notificationPrioritySchema.optional(),
  category: z.string().optional(),
  senderId: z.string().optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  keyword: z.string().optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

// ============================================
// User Notification Schemas
// ============================================

export const userNotificationDtoSchema = z.object({
  id: z.string(),
  messageId: z.string(),
  userId: z.string(),
  subject: z.string(),
  content: z.string(),
  isHtml: z.boolean(),
  isRead: z.boolean(),
  readAt: z.union([z.date(), z.string()]).nullable().optional(),
  isArchived: z.boolean(),
  archivedAt: z.union([z.date(), z.string()]).nullable().optional(),
  senderName: z.string().nullable().optional(),
  category: z.string().nullable().optional(),
  priority: notificationPrioritySchema,
  attachments: z.array(fileInfoSchema),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const markAsReadSchema = z.object({
  notificationIds: z.array(z.string().min(1)).min(1),
});

export const userNotificationQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  isRead: z.boolean().optional(),
  isArchived: z.boolean().optional(),
  category: z.string().optional(),
  priority: notificationPrioritySchema.optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

// ============================================
// Preferences Schemas
// ============================================

export const channelPreferenceSchema = z.object({
  email: z.boolean(),
  sms: z.boolean(),
  push: z.boolean(),
  inApp: z.boolean(),
});

export const notificationPreferencesSchema = z.object({
  userId: z.string(),
  emailEnabled: z.boolean(),
  smsEnabled: z.boolean(),
  pushEnabled: z.boolean(),
  inAppEnabled: z.boolean(),
  emailDigest: z.boolean(),
  digestFrequency: z.enum(['daily', 'weekly', 'none']),
  quietHoursStart: z.string().optional(),
  quietHoursEnd: z.string().optional(),
  categoryPreferences: z.record(channelPreferenceSchema),
});
