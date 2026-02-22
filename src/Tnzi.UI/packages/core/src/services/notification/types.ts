/**
 * Notification Module Types - Message and notification management
 * Aligned with Tnzi.NET backend Notification module
 */

import type { AuditedEntity, FullAuditedEntity } from '../../types/entities';
import type { SortedPagedQueryDto } from '../../types/pagination';
import {
  NotificationType,
  NotificationStatus,
  NotificationPriority,
  RecipientType,
} from './metadata';

export {
  NotificationType,
  NotificationStatus,
  NotificationPriority,
  RecipientType,
};

// ============================================
// Message Types
// ============================================

/**
 * Message DTO
 */
export interface MessageDto extends AuditedEntity<string> {
  type: NotificationType;
  subject: string;
  content: string;
  isHtml: boolean;
  category?: string | null;
  priority: NotificationPriority;
  status: NotificationStatus;
  senderId?: string | null;
  senderName?: string | null;
  templateId?: string | null;
  templateName?: string | null;
  layoutId?: string | null;
  layoutName?: string | null;
  maxRetryCount: number;
  retryCount: number;
  lastError?: string | null;
  scheduledAt?: Date | string | null;
  sentAt?: Date | string | null;
  recipientCount: number;
  attachments: FileInfoDto[];
  recipients: MessageRecipientDto[];
}

/**
 * Message recipient DTO
 */
export interface MessageRecipientDto extends FullAuditedEntity<string> {
  messageId: string;
  recipientType: RecipientType;
  recipientId?: string | null;
  recipientName?: string | null;
  recipientEmail?: string | null;
  recipientPhone?: string | null;
  status: NotificationStatus;
  sentAt?: Date | string | null;
  readAt?: Date | string | null;
  errorMessage?: string | null;
}

/**
 * File info for attachments
 */
export interface FileInfoDto {
  id: string;
  name: string;
  url: string;
  size: number;
  contentType: string;
}

// ============================================
// Request Types
// ============================================

/**
 * Recipient info for create message
 */
export interface RecipientInfoDto {
  type: RecipientType;
  id?: string;
  email?: string;
  phone?: string;
  name?: string;
}

/**
 * Create notification request
 */
export interface CreateNotificationDto {
  type: NotificationType;
  subject: string;
  content: string;
  isHtml?: boolean;
  recipients: RecipientInfoDto[];
  attachments?: string[];
  sendImmediately?: boolean;
  scheduledAt?: Date | string;
  maxRetryCount?: number;
  templateName?: string;
  layoutName?: string;
  templateVariables?: Record<string, unknown>;
  category?: string;
  priority?: NotificationPriority;
  senderId?: string;
}

/**
 * Update notification request (only for pending messages)
 */
export interface UpdateNotificationDto {
  subject?: string;
  content?: string;
  isHtml?: boolean;
  scheduledAt?: Date | string;
  priority?: NotificationPriority;
  category?: string;
}

/**
 * Message list query parameters
 */
export interface MessageListQueryDto extends SortedPagedQueryDto {
  type?: NotificationType;
  status?: NotificationStatus;
  priority?: NotificationPriority;
  category?: string;
  senderId?: string;
  startDate?: Date | string;
  endDate?: Date | string;
  keyword?: string;
}

// ============================================
// User Notification Types
// ============================================

/**
 * User notification (inbox)
 */
export interface UserNotificationDto extends FullAuditedEntity<string> {
  messageId: string;
  userId: string;
  subject: string;
  content: string;
  isHtml: boolean;
  isRead: boolean;
  readAt?: Date | string | null;
  isArchived: boolean;
  archivedAt?: Date | string | null;
  senderName?: string | null;
  category?: string | null;
  priority: NotificationPriority;
  attachments: FileInfoDto[];
}

/**
 * User notification list query
 */
export interface UserNotificationQueryDto extends SortedPagedQueryDto {
  isRead?: boolean;
  isArchived?: boolean;
  category?: string;
  priority?: NotificationPriority;
  startDate?: Date | string;
  endDate?: Date | string;
}

/**
 * Mark as read request
 */
export interface MarkAsReadDto {
  notificationIds: string[];
}

/**
 * Notification preferences
 */
export interface NotificationPreferencesDto {
  userId: string;
  emailEnabled: boolean;
  smsEnabled: boolean;
  pushEnabled: boolean;
  inAppEnabled: boolean;
  emailDigest: boolean;
  digestFrequency: 'daily' | 'weekly' | 'none';
  quietHoursStart?: string;
  quietHoursEnd?: string;
  categoryPreferences: Record<string, NotificationChannelPreference>;
}

/**
 * Channel preference per category
 */
export interface NotificationChannelPreference {
  email: boolean;
  sms: boolean;
  push: boolean;
  inApp: boolean;
}

// ============================================
// Statistics Types
// ============================================

/**
 * Notification statistics
 */
export interface NotificationStatisticsDto {
  totalSent: number;
  totalPending: number;
  totalFailed: number;
  byType: Record<NotificationType, TypeStatistics>;
  byStatus: Record<NotificationStatus, number>;
  byPriority: Record<NotificationPriority, number>;
}

/**
 * Statistics by type
 */
export interface TypeStatistics {
  sent: number;
  failed: number;
  pending: number;
}
