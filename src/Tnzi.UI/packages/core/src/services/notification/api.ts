/**
 * Notification Module API - Message and notification operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  MessageDto,
  CreateNotificationDto,
  MessageListQueryDto,
  NotificationStatisticsDto,
} from './types';

const ADMIN_BASE = '/admin/messages';

/**
 * Admin Notification Management API
 */
export function useAdminNotificationApi(client: HttpClient) {
  return {
    /** Get message list */
    getList: (data?: MessageListQueryDto) =>
      client.post<PagedList<MessageDto>>(`${ADMIN_BASE}/query`, data ?? {}),

    /** Get message by ID */
    getById: (id: string) =>
      client.get<MessageDto>(`${ADMIN_BASE}/${id}`),

    /** Create notification */
    create: (data: CreateNotificationDto) =>
      client.post<MessageDto>(`${ADMIN_BASE}`, data),

    /** Create and send notification */
    createAndSend: (data: CreateNotificationDto) =>
      client.post<MessageDto>(`${ADMIN_BASE}/create-and-send`, data),

    /** Delete notification */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_BASE}/${id}`),

    /** Send notification immediately */
    send: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/send`),

    /** Cancel scheduled notification */
    cancel: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/cancel`),

    /** Retry failed notification */
    retry: (id: string) =>
      client.post<void>(`${ADMIN_BASE}/${id}/retry`),

    /** Get failed notifications */
    getFailed: (startDate?: Date | string, endDate?: Date | string, top?: number) =>
      client.get<MessageDto[]>(`${ADMIN_BASE}/failed`, {
        params: { startDate, endDate, top },
      }),

    /** Get statistics */
    getStatistics: (startDate?: Date | string, endDate?: Date | string) =>
      client.get<NotificationStatisticsDto>(`${ADMIN_BASE}/statistics`, {
        params: { startDate, endDate },
      }),

    /** Get statistics grouped by channel */
    getStatisticsByChannel: (startDate?: Date | string, endDate?: Date | string) =>
      client.get<Array<{ channel: string; count: number }>>(`${ADMIN_BASE}/statistics/by-channel`, {
        params: { startDate, endDate },
      }),

    /** Get statistics grouped by status */
    getStatisticsByStatus: (startDate?: Date | string, endDate?: Date | string) =>
      client.get<Array<{ status: string; count: number }>>(`${ADMIN_BASE}/statistics/by-status`, {
        params: { startDate, endDate },
      }),
  };
}
