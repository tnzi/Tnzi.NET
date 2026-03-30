/**
 * Notification Module Zod Schemas
 * Aligned with Tnzi.NET backend Notification module DTOs
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const notificationTypeSchema = z.number().int().min(1).max(4);
export const notificationStatusSchema = z.number().int().min(0).max(6);
export const notificationPrioritySchema = z.number().int().min(0).max(3);
export const recipientTypeSchema = z.number().int().min(1).max(6);
export const trendIntervalSchema = z.number().int().min(0).max(2);

// ============================================
// Admin - Notification Schemas
// ============================================

export const fileInfoSchema = z.object({
  fileId: z.string().nullable().optional(),
  fileName: z.string(),
  url: z.string().nullable().optional(),
  size: z.number().int().nonnegative().optional(),
  contentType: z.string().nullable().optional(),
});

export const recipientInputSchema = z.object({
  address: z.string().min(1),
  name: z.string().nullable().optional(),
  userId: z.string().nullable().optional(),
});

export const recipientOutputSchema = z.object({
  id: z.string(),
  address: z.string(),
  name: z.string().nullable().optional(),
  status: notificationStatusSchema,
  sentTime: z.string().nullable().optional(),
  failureReason: z.string().nullable().optional(),
  externalMessageId: z.string().nullable().optional(),
  userId: z.string().nullable().optional(),
  isRead: z.boolean(),
  readTime: z.string().nullable().optional(),
});

export const notificationInfoSchema = z.object({
  id: z.string(),
  type: notificationTypeSchema,
  subject: z.string(),
  content: z.string(),
  isHtml: z.boolean(),
  status: notificationStatusSchema,
  sentTime: z.string().nullable().optional(),
  failureReason: z.string().nullable().optional(),
  retryCount: z.number().int().nonnegative(),
  maxRetryCount: z.number().int().nonnegative(),
  totalRecipientCount: z.number().int().nonnegative(),
  successCount: z.number().int().nonnegative(),
  failureCount: z.number().int().nonnegative(),
  creationTime: z.string(),
  priority: notificationPrioritySchema,
  senderId: z.string().nullable().optional(),
  category: z.string(),
  templateName: z.string().nullable().optional(),
  scheduledTime: z.string().nullable().optional(),
  recipients: z.array(recipientOutputSchema),
  attachments: z.array(fileInfoSchema),
});

// ============================================
// Admin - Request Schemas
// ============================================

export const createNotificationRequestSchema = z.object({
  type: notificationTypeSchema,
  subject: z.string().min(1).max(200),
  content: z.string().min(1),
  isHtml: z.boolean().optional(),
  recipients: z.array(recipientInputSchema).min(1),
  attachments: z.array(fileInfoSchema).optional(),
  sendImmediately: z.boolean().optional(),
  maxRetryCount: z.number().int().min(0).max(10).optional(),
  templateName: z.string().optional(),
  layoutName: z.string().optional(),
  templateVariables: z.record(z.unknown()).optional(),
  category: z.string().max(50).optional(),
  priority: notificationPrioritySchema.optional(),
  senderId: z.string().optional(),
  scheduledTime: z.string().optional(),
});

export const queryNotificationRequestSchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  type: notificationTypeSchema.optional(),
  status: notificationStatusSchema.optional(),
  startTime: z.string().optional(),
  endTime: z.string().optional(),
  keyword: z.string().optional(),
  category: z.string().optional(),
  priority: notificationPrioritySchema.optional(),
  senderId: z.string().optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

// ============================================
// Admin - Statistics Schemas
// ============================================

export const notificationStatisticsSchema = z.object({
  totalNotifications: z.number().int().nonnegative(),
  sentCount: z.number().int().nonnegative(),
  sendingCount: z.number().int().nonnegative(),
  failedCount: z.number().int().nonnegative(),
  pendingCount: z.number().int().nonnegative(),
  cancelledCount: z.number().int().nonnegative(),
  successRate: z.number().min(0).max(1),
});

export const channelStatisticsSchema = z.object({
  channel: notificationTypeSchema,
  totalCount: z.number().int().nonnegative(),
  successCount: z.number().int().nonnegative(),
  failedCount: z.number().int().nonnegative(),
  successRate: z.number().min(0).max(1),
});

export const statusStatisticsSchema = z.object({
  status: notificationStatusSchema,
  count: z.number().int().nonnegative(),
  percentage: z.number().min(0).max(100),
});

export const trendDataPointSchema = z.object({
  label: z.string(),
  startTime: z.string(),
  totalCount: z.number().int().nonnegative(),
  sentCount: z.number().int().nonnegative(),
  failedCount: z.number().int().nonnegative(),
});

export const notificationTrendSchema = z.object({
  interval: trendIntervalSchema,
  startDate: z.string(),
  endDate: z.string(),
  dataPoints: z.array(trendDataPointSchema),
});

export const notificationPreviewSchema = z.object({
  subject: z.string(),
  content: z.string(),
  isHtml: z.boolean(),
  category: z.string(),
  recipientCount: z.number().int().nonnegative(),
  templateName: z.string().nullable().optional(),
});

export const deliveryReportSchema = z.object({
  messageId: z.string(),
  subject: z.string(),
  type: notificationTypeSchema,
  totalRecipients: z.number().int().nonnegative(),
  sentCount: z.number().int().nonnegative(),
  failedCount: z.number().int().nonnegative(),
  pendingCount: z.number().int().nonnegative(),
  readCount: z.number().int().nonnegative(),
  successRate: z.number().min(0).max(1),
  recipients: z.array(recipientOutputSchema),
});

// ============================================
// User Notification Schemas
// ============================================

export const userNotificationItemSchema = z.object({
  id: z.string(),
  type: notificationTypeSchema,
  subject: z.string(),
  category: z.string(),
  priority: notificationPrioritySchema,
  isRead: z.boolean(),
  readTime: z.string().nullable().optional(),
  creationTime: z.string(),
});

export const userNotificationDetailSchema = userNotificationItemSchema.extend({
  content: z.string(),
  isHtml: z.boolean(),
});

export const queryUserNotificationRequestSchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  type: notificationTypeSchema.optional(),
  isRead: z.boolean().optional(),
  category: z.string().optional(),
  priority: notificationPrioritySchema.optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

export const unreadCountSchema = z.object({
  totalUnread: z.number().int().nonnegative(),
  unreadByCategory: z.record(z.number().int().nonnegative()),
});
