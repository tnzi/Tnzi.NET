/**
 * Audit Module API - Audit operation tracking
 * Aligned with backend DefaultAuditOperationAdminController (11 endpoints)
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  AuditOperationDto,
  AuditOperationQueryDto,
  AuditOperationStatisticsDto,
  AuditTrendPointDto,
  TopFunctionDto,
  TopUserDto,
} from './types';
import type { AuditTrendGroupBy } from './metadata';

const ADMIN_BASE = '/admin/audit-operations';

/**
 * Admin Audit API
 */
export function useAdminAuditApi(client: HttpClient) {
  return {
    /** Get audit operation by ID */
    getById: (id: string) =>
      client.get<AuditOperationDto>(`${ADMIN_BASE}/${id}`),

    /** Get audit operation list (paged) */
    getList: (data?: AuditOperationQueryDto) =>
      client.post<PagedList<AuditOperationDto>>(`${ADMIN_BASE}/query`, data ?? {}),

    /** Get user operation logs */
    getUserOperations: (
      userId: string,
      startDate?: Date | string,
      endDate?: Date | string,
      resultType?: number,
    ) =>
      client.get<AuditOperationDto[]>(`${ADMIN_BASE}/user/${userId}`, {
        params: { startDate, endDate, resultType },
      }),

    /** Get function statistics */
    getFunctionStatistics: (
      functionName: string,
      startDate?: Date | string,
      endDate?: Date | string,
    ) =>
      client.get<AuditOperationStatisticsDto>(
        `${ADMIN_BASE}/statistics/function/${encodeURIComponent(functionName)}`,
        { params: { startDate, endDate } },
      ),

    /** Get user statistics */
    getUserStatistics: (
      userId: string,
      startDate?: Date | string,
      endDate?: Date | string,
    ) =>
      client.get<AuditOperationStatisticsDto>(
        `${ADMIN_BASE}/statistics/user/${userId}`,
        { params: { startDate, endDate } },
      ),

    /** Delete expired audit operations */
    deleteExpired: (days?: number) =>
      client.delete<number>(`${ADMIN_BASE}/expired`, {
        params: { days },
      }),

    /** Get audit operation trend statistics */
    getTrend: (
      startDate: Date | string,
      endDate: Date | string,
      groupBy?: AuditTrendGroupBy,
    ) =>
      client.get<AuditTrendPointDto[]>(`${ADMIN_BASE}/trend`, {
        params: { startDate, endDate, groupBy },
      }),

    /** Get top N functions by hit count */
    getTopFunctions: (
      topN?: number,
      startDate?: Date | string,
      endDate?: Date | string,
    ) =>
      client.get<TopFunctionDto[]>(`${ADMIN_BASE}/top-functions`, {
        params: { topN, startDate, endDate },
      }),

    /** Get top N active users */
    getTopUsers: (
      topN?: number,
      startDate?: Date | string,
      endDate?: Date | string,
    ) =>
      client.get<TopUserDto[]>(`${ADMIN_BASE}/top-users`, {
        params: { topN, startDate, endDate },
      }),

    /** Export audit operations as CSV (Blob download; backend emits UTF-8 BOM) */
    exportCsv: (query: AuditOperationQueryDto) =>
      client.download(`${ADMIN_BASE}/export/csv`, { method: 'POST', body: query }),

    /** Export audit operations as JSON (Blob download) */
    exportJson: (query: AuditOperationQueryDto) =>
      client.download(`${ADMIN_BASE}/export/json`, { method: 'POST', body: query }),
  };
}
