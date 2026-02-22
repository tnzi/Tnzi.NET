/**
 * Audit Module API - Audit operation tracking
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  AuditLogDto,
  AuditLogQueryDto,
  AuditStatisticsDto,
} from './types';

const ADMIN_BASE = '/admin/audit-operations';

/**
 * Admin Audit API
 */
export function useAdminAuditApi(client: HttpClient) {
  return {
    /** Get audit log list */
    getList: (data?: AuditLogQueryDto) =>
      client.post<PagedList<AuditLogDto>>(`${ADMIN_BASE}/query`, data ?? {}),

    /** Get audit log by ID */
    getById: (id: string) =>
      client.get<AuditLogDto>(`${ADMIN_BASE}/${id}`),

    /** Get user operation logs */
    getUserOperations: (userId: string, startDate?: Date | string, endDate?: Date | string, resultType?: number) =>
      client.get<AuditLogDto[]>(`${ADMIN_BASE}/user/${userId}`, {
        params: { startDate, endDate, resultType },
      }),

    /** Get function statistics */
    getFunctionStatistics: (functionName: string, startDate?: Date | string, endDate?: Date | string) =>
      client.get<AuditStatisticsDto>(`${ADMIN_BASE}/statistics/function/${encodeURIComponent(functionName)}`, {
        params: { startDate, endDate },
      }),

    /** Get user statistics */
    getUserStatistics: (userId: string, startDate?: Date | string, endDate?: Date | string) =>
      client.get<AuditStatisticsDto>(`${ADMIN_BASE}/statistics/user/${userId}`, {
        params: { startDate, endDate },
      }),

    /** Delete expired audit logs */
    deleteExpired: (days?: number) =>
      client.delete<number>(`${ADMIN_BASE}/expired`, {
        params: { days },
      }),
  };
}
