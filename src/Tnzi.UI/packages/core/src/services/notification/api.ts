/**
 * Notification Module API - Admin management and user inbox operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  NotificationInfo,
  CreateNotificationRequest,
  QueryNotificationRequest,
  NotificationStatisticsDto,
  ChannelStatisticsDto,
  StatusStatisticsDto,
  NotificationTrendDto,
  NotificationPreviewDto,
  DeliveryReportDto,
  UserNotificationItem,
  UserNotificationDetail,
  QueryUserNotificationRequest,
  UnreadCountDto,
  NotificationPreferenceDto,
  NotificationPreferenceQueryDto,
  SetNotificationPreferenceDto,
} from './types';
import type { TrendInterval } from './metadata';

const ADMIN_BASE = '/admin/notifications';
const PREFERENCES_BASE = '/admin/notification-preferences';
const USER_BASE = '/notifications';

/**
 * Admin Notification Management API
 */
export function useAdminNotificationApi(client: HttpClient) {
  return {
    /** Get notification by ID */
    getById: (id: string) =>
      client.get<NotificationInfo>(`${ADMIN_BASE}/${id}`),

    /** Create notification (without sending) */
    create: (data: CreateNotificationRequest) =>
      client.post<NotificationInfo>(`${ADMIN_BASE}`, data),

    /** Create and send notification */
    createAndSend: (data: CreateNotificationRequest) =>
      client.post<NotificationInfo>(`${ADMIN_BASE}/create-and-send`, data),

    /** Send notification immediately */
    send: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/send`),

    /** Query notifications */
    query: (data?: QueryNotificationRequest) =>
      client.post<PagedList<NotificationInfo>>(`${ADMIN_BASE}/query`, data ?? {}),

    /** Retry failed notification */
    retry: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/retry`),

    /** Retry all failed notifications in date range */
    retryFailed: (startDate?: string, endDate?: string) =>
      client.post<void>(`${ADMIN_BASE}/retry-failed`, undefined, {
        params: { startDate, endDate },
      }),

    /** Cancel notification */
    cancel: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/cancel`),

    /** Delete notification */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_BASE}/${id}`),

    /** Get statistics */
    getStatistics: (startDate?: string, endDate?: string) =>
      client.get<NotificationStatisticsDto>(`${ADMIN_BASE}/statistics`, {
        params: { startDate, endDate },
      }),

    /** Get statistics by channel */
    getStatisticsByChannel: (startDate?: string, endDate?: string) =>
      client.get<ChannelStatisticsDto[]>(`${ADMIN_BASE}/statistics/by-channel`, {
        params: { startDate, endDate },
      }),

    /** Get statistics by status */
    getStatisticsByStatus: (startDate?: string, endDate?: string) =>
      client.get<StatusStatisticsDto[]>(`${ADMIN_BASE}/statistics/by-status`, {
        params: { startDate, endDate },
      }),

    /** Get failed notifications */
    getFailed: (startDate?: string, endDate?: string, top?: number) =>
      client.get<NotificationInfo[]>(`${ADMIN_BASE}/failed`, {
        params: { startDate, endDate, top },
      }),

    /** Batch cancel notifications */
    batchCancel: (ids: string[]) =>
      client.post<number>(`${ADMIN_BASE}/batch-cancel`, ids),

    /** Batch delete notifications */
    batchDelete: (ids: string[]) =>
      client.delete<number>(`${ADMIN_BASE}/batch`, { body: ids }),

    /** Query scheduled notifications */
    getScheduled: (data?: QueryNotificationRequest) =>
      client.post<PagedList<NotificationInfo>>(`${ADMIN_BASE}/scheduled`, data ?? {}),

    /** Get statistics trend */
    getStatisticsTrend: (interval?: TrendInterval, startDate?: string, endDate?: string) =>
      client.get<NotificationTrendDto>(`${ADMIN_BASE}/statistics/trend`, {
        params: { interval, startDate, endDate },
      }),

    /** Preview notification rendering (without creating or sending) */
    preview: (data: CreateNotificationRequest) =>
      client.post<NotificationPreviewDto>(`${ADMIN_BASE}/preview`, data),

    /** Resend to failed recipients */
    resendToFailed: (id: string) =>
      client.post<number>(`${ADMIN_BASE}/${id}/resend-failed`),

    /** Get delivery report */
    getDeliveryReport: (id: string) =>
      client.get<DeliveryReportDto>(`${ADMIN_BASE}/${id}/delivery-report`),
  };
}

/**
 * Admin Notification Preference (Subscription) Management API.
 * Backend: DefaultNotificationPreferenceAdminController.
 */
export function useAdminNotificationPreferenceApi(client: HttpClient) {
  return {
    /** Canonical paged list across all users (GET /admin/notification-preferences) */
    getPagedList: (query?: NotificationPreferenceQueryDto) =>
      client.get<PagedList<NotificationPreferenceDto>>(PREFERENCES_BASE, { params: query }),

    /** Get all preferences for a specific user */
    getByUser: (userId: string) =>
      client.get<NotificationPreferenceDto[]>(`${PREFERENCES_BASE}/user/${userId}`),

    /** Upsert a preference for the given user (PUT /admin/notification-preferences/user/{userId}) */
    setPreference: (userId: string, data: SetNotificationPreferenceDto) =>
      client.put<NotificationPreferenceDto>(`${PREFERENCES_BASE}/user/${userId}`, data),

    /** Delete a preference by id */
    delete: (id: string) =>
      client.delete<void>(`${PREFERENCES_BASE}/${id}`),

    /** Reset a user's preferences to platform defaults */
    resetToDefault: (userId: string) =>
      client.post<void>(`${PREFERENCES_BASE}/user/${userId}/reset`),
  };
}

/**
 * User Notification API (Inbox)
 */
export function useNotificationApi(client: HttpClient) {
  return {
    /** Get current user's notification inbox */
    getInbox: (data?: QueryUserNotificationRequest) =>
      client.post<PagedList<UserNotificationItem>>(`${USER_BASE}/inbox`, data ?? {}),

    /** Get notification detail (auto marks as read) */
    getDetail: (id: string) =>
      client.get<UserNotificationDetail>(`${USER_BASE}/${id}`),

    /** Mark notification as read */
    markAsRead: (id: string) =>
      client.post<void>(`${USER_BASE}/${id}/read`),

    /** Mark all notifications as read */
    markAllAsRead: () =>
      client.post<void>(`${USER_BASE}/read-all`),

    /** Get unread notification count */
    getUnreadCount: () =>
      client.get<UnreadCountDto>(`${USER_BASE}/unread-count`),

    /** Delete notification */
    delete: (id: string) =>
      client.delete<void>(`${USER_BASE}/${id}`),

    /** Batch mark notifications as read */
    batchMarkAsRead: (ids: string[]) =>
      client.post<void>(`${USER_BASE}/batch-read`, ids),

    /** Batch delete notifications */
    batchDelete: (ids: string[]) =>
      client.post<void>(`${USER_BASE}/batch-delete`, ids),
  };
}
