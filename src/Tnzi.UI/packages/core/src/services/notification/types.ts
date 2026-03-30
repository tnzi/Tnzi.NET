/**
 * Notification Module Types - Message and notification management
 * Aligned with Tnzi.NET backend Notification module
 */

import type { SortedPagedQueryDto } from '../../types/pagination';
import {
  NotificationType,
  NotificationStatus,
  NotificationPriority,
  TrendInterval,
} from './metadata';

export {
  NotificationType,
  NotificationStatus,
  NotificationPriority,
  TrendInterval,
};

// ============================================
// Admin - Notification Types
// ============================================

/**
 * Recipient input (for creating notifications)
 * Backend: RecipientInput
 */
export interface RecipientInput {
  address: string;
  name?: string | null;
  userId?: string | null;
}

/**
 * Recipient output (for querying notifications)
 * Backend: RecipientOutput
 */
export interface RecipientOutput {
  id: string;
  address: string;
  name?: string | null;
  status: NotificationStatus;
  sentTime?: string | null;
  failureReason?: string | null;
  externalMessageId?: string | null;
  userId?: string | null;
  isRead: boolean;
  readTime?: string | null;
}

/**
 * File info for attachments
 * Backend: Tnzi.Storage.FileInfoDto
 */
export interface FileInfoDto {
  fileId?: string | null;
  fileName: string;
  url?: string | null;
  size?: number;
  contentType?: string | null;
}

/**
 * Notification info (query result)
 * Backend: NotificationInfo
 */
export interface NotificationInfo {
  id: string;
  type: NotificationType;
  subject: string;
  content: string;
  isHtml: boolean;
  status: NotificationStatus;
  sentTime?: string | null;
  failureReason?: string | null;
  retryCount: number;
  maxRetryCount: number;
  totalRecipientCount: number;
  successCount: number;
  failureCount: number;
  creationTime: string;
  priority: NotificationPriority;
  senderId?: string | null;
  category: string;
  templateName?: string | null;
  scheduledTime?: string | null;
  recipients: RecipientOutput[];
  attachments: FileInfoDto[];
}

/**
 * Create notification request
 * Backend: CreateNotificationRequest
 */
export interface CreateNotificationRequest {
  type: NotificationType;
  subject: string;
  content: string;
  isHtml?: boolean;
  recipients: RecipientInput[];
  attachments?: FileInfoDto[];
  sendImmediately?: boolean;
  maxRetryCount?: number;
  templateName?: string;
  layoutName?: string;
  templateVariables?: Record<string, unknown>;
  category?: string;
  priority?: NotificationPriority;
  senderId?: string;
  scheduledTime?: string;
}

/**
 * Query notification request (admin)
 * Backend: QueryNotificationRequest extends PagedQueryDto
 */
export interface QueryNotificationRequest extends SortedPagedQueryDto {
  type?: NotificationType;
  status?: NotificationStatus;
  startTime?: string;
  endTime?: string;
  keyword?: string;
  category?: string;
  priority?: NotificationPriority;
  senderId?: string;
}

/**
 * Notification preview result
 * Backend: NotificationPreviewDto
 */
export interface NotificationPreviewDto {
  subject: string;
  content: string;
  isHtml: boolean;
  category: string;
  recipientCount: number;
  templateName?: string | null;
}

/**
 * Delivery report
 * Backend: DeliveryReportDto
 */
export interface DeliveryReportDto {
  messageId: string;
  subject: string;
  type: NotificationType;
  totalRecipients: number;
  sentCount: number;
  failedCount: number;
  pendingCount: number;
  readCount: number;
  successRate: number;
  recipients: RecipientOutput[];
}

// ============================================
// Admin - Statistics Types
// ============================================

/**
 * Notification statistics
 * Backend: NotificationStatisticsDto
 */
export interface NotificationStatisticsDto {
  totalNotifications: number;
  sentCount: number;
  sendingCount: number;
  failedCount: number;
  pendingCount: number;
  cancelledCount: number;
  successRate: number;
}

/**
 * Channel statistics
 * Backend: ChannelStatisticsDto
 */
export interface ChannelStatisticsDto {
  channel: NotificationType;
  totalCount: number;
  successCount: number;
  failedCount: number;
  successRate: number;
}

/**
 * Status statistics
 * Backend: StatusStatisticsDto
 */
export interface StatusStatisticsDto {
  status: NotificationStatus;
  count: number;
  percentage: number;
}

/**
 * Trend data point
 * Backend: TrendDataPoint
 */
export interface TrendDataPoint {
  label: string;
  startTime: string;
  totalCount: number;
  sentCount: number;
  failedCount: number;
}

/**
 * Notification trend
 * Backend: NotificationTrendDto
 */
export interface NotificationTrendDto {
  interval: TrendInterval;
  startDate: string;
  endDate: string;
  dataPoints: TrendDataPoint[];
}

// ============================================
// User Notification Types (Inbox)
// ============================================

/**
 * User notification inbox item
 * Backend: UserNotificationItem
 */
export interface UserNotificationItem {
  id: string;
  type: NotificationType;
  subject: string;
  category: string;
  priority: NotificationPriority;
  isRead: boolean;
  readTime?: string | null;
  creationTime: string;
}

/**
 * User notification detail (includes content)
 * Backend: UserNotificationDetail extends UserNotificationItem
 */
export interface UserNotificationDetail extends UserNotificationItem {
  content: string;
  isHtml: boolean;
}

/**
 * Query user notification request
 * Backend: QueryUserNotificationRequest extends PagedQueryDto
 */
export interface QueryUserNotificationRequest extends SortedPagedQueryDto {
  type?: NotificationType;
  isRead?: boolean;
  category?: string;
  priority?: NotificationPriority;
}

/**
 * Unread count
 * Backend: UnreadCountDto
 */
export interface UnreadCountDto {
  totalUnread: number;
  unreadByCategory: Record<string, number>;
}
