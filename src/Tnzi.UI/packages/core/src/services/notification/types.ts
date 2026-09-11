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
  /**
   * Quiet-hours deferral: delivery was held until this instant instead of being
   * dropped. The value survives delivery, so it must be read together with
   * `status` - a `Sent` row that carries one was delayed, not late.
   */
  deferredUntil?: string | null;
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
  /** Transactional message: exempt from the opt-out list. */
  isTransactional: boolean;
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
  /**
   * Transactional message (as opposed to commercial/bulk): exempt from the
   * opt-out list. Defaults to `false`.
   *
   * Set this for password resets, verification codes, invoices and billing
   * notices - the unsubscribe button governs marketing mail, and it must not
   * leave someone unable to receive a login code. When in doubt, leave it
   * unset: the default treats the message as commercial, which errs towards
   * sending one message fewer rather than making an unsubscribe meaningless.
   */
  isTransactional?: boolean;
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

/**
 * What a one-click unsubscribe link would act on.
 * Backend: UnsubscribePreviewDto
 */
export interface UnsubscribePreviewDto {
  /**
   * Masked address (`a***@example.com`). The link may have been forwarded, so
   * the full address is never echoed back - the mask keeps just enough for the
   * recipient to recognise themselves.
   */
  maskedAddress: string;
  channel: NotificationType;
  /** null means the whole channel, not one category of it. */
  category?: string | null;
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

// ─── Notification Preference (aka Subscription) ──────────────────────────────

/**
 * User notification preference read DTO. Backend: NotificationPreferenceDto.
 * The Preference entity IS the subscription model in Tnzi.Notification -
 * it controls per-channel+category opt-in and carries quiet-hours / rate-limit.
 */
export interface NotificationPreferenceDto {
  id: string;
  userId: string;
  /** Channel name: Email, Sms, InApp, Webhook */
  channel: string;
  /** Notification category; null = global preference for the channel */
  category?: string;
  isEnabled: boolean;
  /** Quiet hours start time (UTC, "HH:mm:ss") */
  quietHoursStart?: string;
  /** Quiet hours end time (UTC, "HH:mm:ss") */
  quietHoursEnd?: string;
  maxFrequencyPerHour?: number;
}

/**
 * Paged query DTO for GET /admin/notification-preferences.
 * Supports optional filters on user / channel / category / enabled state.
 */
export interface NotificationPreferenceQueryDto {
  pageIndex?: number;
  pageSize?: number;
  orderBy?: string;
  userId?: string;
  channel?: string;
  category?: string;
  isEnabled?: boolean;
}

/**
 * Upsert input DTO for PUT /admin/notification-preferences/user/{userId}.
 * Backend: SetNotificationPreferenceDto.
 */
export interface SetNotificationPreferenceDto {
  channel: string;
  category?: string;
  isEnabled: boolean;
  quietHoursStart?: string;
  quietHoursEnd?: string;
  maxFrequencyPerHour?: number;
}

/**
 * Platform a registered push device runs on.
 *
 * Does NOT pick a delivery route: all three go out through FCM (iOS via FCM's
 * APNs relay). It is for recognising and counting devices only.
 * Backend: DevicePlatform (Tnzi.Notification.Push).
 */
export const DevicePlatform = {
  Android: 1,
  Ios: 2,
  Web: 3,
} as const;

export type DevicePlatform = (typeof DevicePlatform)[keyof typeof DevicePlatform];

/**
 * Input for POST /notifications/devices.
 *
 * Upsert semantics - call it on every app start. FCM re-issues the token on
 * reinstall, data clear and rotation, and the client cannot know whether the
 * server has seen the current one.
 */
export interface RegisterPushDeviceDto {
  token: string;
  platform: DevicePlatform;
  deviceName?: string;
  /**
   * Platform-reported device identifier (iOS `identifierForVendor`, a Firebase
   * installation id, Android SSAID...). Optional.
   *
   * ★ For RECOGNITION ONLY - it addresses nothing. It is a value the client
   * asserts and the server cannot verify, and it travels through business
   * tables, logs and the admin UI. Addressing by it would make hijacking a
   * device's push address a single ordinary request. Delivery is addressed by
   * `userId` or by the device key alone.
   */
  externalDeviceId?: string;
}

/**
 * Input for POST /notifications/devices/unregister (sign-out).
 *
 * The token travels in the body, not the query string: a query string is
 * copied verbatim into access logs and reverse-proxy logs, and this value IS
 * the "which device has this app installed" fact.
 */
export interface UnregisterPushDeviceDto {
  token: string;
}

/**
 * A registered push device.
 *
 * `tokenMask` is a masked tail (e.g. `…a1b2c3d4`), never the full token: the
 * list, not the token, is the sensitive part - one query would otherwise export
 * "who has this app installed on which devices". Unregister by `id`.
 */
export interface PushDeviceDto {
  id: string;
  /**
   * The user signed in on this device, or `undefined` for an anonymous device.
   *
   * A row carries two addressing dimensions that can coexist: the signed-in
   * user, and the device's own anonymous identity. An app with no accounts at
   * all only ever produces rows without a `userId`.
   */
  userId?: string;
  tokenMask: string;
  platform: DevicePlatform;
  deviceName?: string;
  /** Platform-reported identifier, for recognition only - it addresses nothing. */
  externalDeviceId?: string;
  lastSeenAt: string;
  creationTime: string;
}

/**
 * What the server hands back when an anonymous device registers.
 *
 * ★ `deviceKey` is plaintext exactly once and is never retrievable again - only
 * its hash is stored. Persist it in the platform's secure storage (iOS Keychain
 * / Android Keystore) right away: losing it loses this device's identity, and
 * there is deliberately no recovery endpoint, because such an endpoint would be
 * a bypass around the key itself.
 *
 * ★ `deviceId` is public and `deviceKey` is private, and they do different jobs.
 * The id goes into business records as the ownership key ("which device
 * submitted this form") and travels through the server, the admin UI and logs;
 * the key only ever proves "I am that device". Knowing the id does not let
 * anyone re-point the row, which is the whole reason they are separate values.
 */
export interface AnonymousDeviceRegistrationDto {
  /** Always present. The client does not need to store it - every call returns it. */
  deviceId: string;
  /**
   * Present ONLY on the call that issued it; absent when you passed a key in.
   *
   * ★ That is what lets registration and refresh be the same call: pass the key
   * if you have one, store the key if one comes back. The client never has to
   * decide whether this is a first launch.
   */
  deviceKey?: string;
}

/** Paged query for GET /admin/notification-devices. */
export interface PushDeviceQueryDto {
  pageIndex?: number;
  pageSize?: number;
  orderBy?: string;
  userId?: string;
  platform?: DevicePlatform;
  lastSeenAfter?: string;
  /**
   * `true` shows only anonymous devices, `false` only devices with a signed-in
   * user; omit to show both.
   *
   * Without this filter an anonymous device can only be spotted by an empty
   * user column, which on screen is indistinguishable from a page that has not
   * finished loading.
   */
  anonymousOnly?: boolean;
  /**
   * Exact match on the platform-reported device identifier.
   *
   * For the case where an operator only has the identifier a user read out to
   * them: the token comes back masked and the client never sees the row id.
   * This is a lookup filter, not an addressing path.
   */
  externalDeviceId?: string;
}
