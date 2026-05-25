/**
 * Performance bridge — wraps `/admin/performance/*` exposed by
 * `Tnzi.Performance.Controllers.DefaultPerformanceAdminController`.
 *
 * `Tnzi.Performance` is an optional infrastructure module. When the host app
 * doesn't load it, `ConditionalControllerProvider` drops the controller, all
 * endpoints return 404, and the bridge surfaces empty data via the page's
 * catch-and-fallback so the menu entry can stay registered without breaking.
 */
import type { HttpClient } from '@tnzi/core/http'

export interface PercentileResultDto {
  p50: number
  p95: number
  p99: number
  average: number
  min: number
  max: number
  sampleCount: number
}

export interface EndpointStatsDto {
  path: string
  method: string
  requestCount: number
  averageDurationMs: number
  p95DurationMs: number
  minDurationMs: number
  maxDurationMs: number
  errorCount: number
  lastRequestTime: string
}

export interface SlowRequestRecordDto {
  path: string
  method: string
  statusCode: number
  durationMs: number
  userId?: string | null
  requestId?: string | null
  timestamp: string
}

export interface PerformanceBridgeDeps {
  client?: HttpClient
}

export interface PerformanceBridge {
  getSummary(minutes?: number): Promise<PercentileResultDto | null>
  getEndpoints(minutes?: number, topN?: number): Promise<EndpointStatsDto[]>
  getSlowRequests(count?: number, thresholdMs?: number): Promise<SlowRequestRecordDto[]>
  clear(): Promise<void>
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
}

export function createPerformanceBridge(deps: PerformanceBridgeDeps = {}): PerformanceBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createPerformanceBridge: no HttpClient provided'))
    return {
      getSummary: noOp as never,
      getEndpoints: noOp as never,
      getSlowRequests: noOp as never,
      clear: noOp as never,
    }
  }

  return {
    getSummary: async (minutes = 60) =>
      unwrap<PercentileResultDto | null>(
        await client.get<PercentileResultDto>(`/admin/performance/summary?minutes=${minutes}`),
      ),
    getEndpoints: async (minutes = 60, topN = 0) =>
      unwrap<EndpointStatsDto[]>(
        await client.get<EndpointStatsDto[]>(`/admin/performance/endpoints?minutes=${minutes}&topN=${topN}`),
      ) ?? [],
    getSlowRequests: async (count = 20, thresholdMs?: number) => {
      const qs = thresholdMs != null
        ? `?count=${count}&thresholdMs=${thresholdMs}`
        : `?count=${count}`
      return (
        unwrap<SlowRequestRecordDto[]>(
          await client.get<SlowRequestRecordDto[]>(`/admin/performance/slow-requests${qs}`),
        ) ?? []
      )
    },
    clear: async () => {
      await client.delete('/admin/performance')
    },
  }
}
