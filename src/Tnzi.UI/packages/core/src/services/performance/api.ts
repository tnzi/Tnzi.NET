/**
 * Performance Module API — admin read access to runtime request timings.
 *
 * Mirrors `Tnzi.Performance/Controllers/DefaultPerformanceAdminController` —
 * percentile summary, per-endpoint stats, slow-request log (+ one DELETE to
 * reset the collectors) under `/admin/performance/*`. `Tnzi.Performance` is an
 * optional infrastructure module, so every endpoint returns 404 when it is not
 * loaded by the host app.
 */

import type { HttpClient } from '../../http/http';
import type {
  PercentileResultDto,
  EndpointStatsDto,
  SlowRequestRecordDto,
} from './types';

const ADMIN_PERFORMANCE_BASE = '/admin/performance';

/**
 * Admin Performance API — request-timing percentiles, per-endpoint stats and
 * the slow-request log.
 *
 * Example:
 * ```ts
 * const api = useAdminPerformanceApi(client);
 * const summary = await api.getSummary(60);
 * const endpoints = await api.getEndpoints(60, 0);
 * const slow = await api.getSlowRequests(20, 500);
 * await api.clear();
 * ```
 */
export function useAdminPerformanceApi(client: HttpClient) {
  return {
    /** Percentile breakdown of request durations over the last `minutes`. */
    getSummary: (minutes = 60) =>
      client.get<PercentileResultDto>(`${ADMIN_PERFORMANCE_BASE}/summary?minutes=${minutes}`),

    /** Per-endpoint aggregate stats; `topN = 0` returns all endpoints. */
    getEndpoints: (minutes = 60, topN = 0) =>
      client.get<EndpointStatsDto[]>(`${ADMIN_PERFORMANCE_BASE}/endpoints?minutes=${minutes}&topN=${topN}`),

    /** Slow-request log; `thresholdMs` is optional (server default applies). */
    getSlowRequests: (count = 20, thresholdMs?: number) =>
      client.get<SlowRequestRecordDto[]>(
        thresholdMs != null
          ? `${ADMIN_PERFORMANCE_BASE}/slow-requests?count=${count}&thresholdMs=${thresholdMs}`
          : `${ADMIN_PERFORMANCE_BASE}/slow-requests?count=${count}`,
      ),

    /** Reset all in-memory performance collectors. */
    clear: () =>
      client.delete(`${ADMIN_PERFORMANCE_BASE}`),
  };
}
