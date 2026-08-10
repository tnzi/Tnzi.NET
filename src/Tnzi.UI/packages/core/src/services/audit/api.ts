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
  DataDestructionDto,
  DataDestructionQueryDto,
  DataDestructionRunDto,
  RecordAccessDto,
  RecordAccessQueryDto,
  RecordAccessUserStatDto,
  TopFunctionDto,
  TopUserDto,
} from './types';
import type { AuditTrendGroupBy } from './metadata';

const ADMIN_BASE = '/admin/audit-operations';
const RECORD_ACCESS_BASE = '/admin/record-access';
const DESTRUCTION_BASE = '/admin/data-destruction';

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

/**
 * Admin record-level read audit API.
 *
 * Backend: DefaultRecordAccessAdminController (permission audit.recordAccess.view).
 * Returns empty pages rather than errors when the capability is switched off,
 * so callers do not have to probe the backend configuration first.
 */
export function useAdminRecordAccessApi(client: HttpClient) {
  return {
    /** Paged read trail - filter by record to get "who read this", by user to get "what they read". */
    getList: (data?: RecordAccessQueryDto) =>
      client.post<PagedList<RecordAccessDto>>(`${RECORD_ACCESS_BASE}/query`, data ?? {}),

    /** Read volume per user, highest first. */
    getUserStatistics: (
      startTime?: Date | string,
      endTime?: Date | string,
      topN?: number,
    ) =>
      client.get<RecordAccessUserStatDto[]>(`${RECORD_ACCESS_BASE}/user-statistics`, {
        params: { startTime, endTime, topN },
      }),

    /** Verify one user's chain; omit userId for the anonymous chain. */
    verify: (userId?: string) =>
      client.get<void>(`${RECORD_ACCESS_BASE}/verify`, { params: { userId } }),
  };
}

/**
 * Admin data destruction API.
 *
 * Backend: DefaultDataDestructionAdminController. Reading certificates needs
 * audit.destruction.view; triggering a run additionally needs
 * audit.destruction.execute - it permanently deletes data.
 */
export function useAdminDataDestructionApi(client: HttpClient) {
  return {
    /** Paged destruction certificates, newest first. */
    getList: (data?: DataDestructionQueryDto) =>
      client.post<PagedList<DataDestructionDto>>(`${DESTRUCTION_BASE}/query`, data ?? {}),

    /** Verify the certificate chain; fails with 409 at the first broken sequence. */
    verify: () => client.get<void>(`${DESTRUCTION_BASE}/verify`),

    /** Run one destruction cycle now. Respects the backend's DryRun setting. */
    run: () => client.post<DataDestructionRunDto>(`${DESTRUCTION_BASE}/run`, {}),
  };
}
