/**
 * Performance bridge — delegates to `useAdminPerformanceApi`
 * (from `@tnzi/core/services/performance`) so admin pages get the standard
 * dependency-injection + single-mock-seam pattern other bridges use.
 *
 * `Tnzi.Performance` is an optional infrastructure module. When the host app
 * doesn't load it, `ConditionalControllerProvider` drops the controller, all
 * endpoints return 404, and the bridge surfaces empty data via the page's
 * catch-and-fallback so the menu entry can stay registered without breaking.
 *
 * DTO types are re-exported below so existing page imports keep resolving
 * after the contract moved into `@tnzi/core`.
 */
import {
  useAdminPerformanceApi,
  type PercentileResultDto,
  type EndpointStatsDto,
  type SlowRequestRecordDto,
} from '@tnzi/core/services/performance'
import { ensureOk, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminPerformanceApi>[0]

export type {
  PercentileResultDto,
  EndpointStatsDto,
  SlowRequestRecordDto,
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

  const api = useAdminPerformanceApi(client)

  return {
    getSummary: async (minutes = 60) =>
      unwrap<PercentileResultDto | null>(await api.getSummary(minutes)),
    getEndpoints: async (minutes = 60, topN = 0) =>
      unwrap<EndpointStatsDto[]>(await api.getEndpoints(minutes, topN)) ?? [],
    getSlowRequests: async (count = 20, thresholdMs?: number) =>
      unwrap<SlowRequestRecordDto[]>(await api.getSlowRequests(count, thresholdMs)) ?? [],
    clear: async () => {
      ensureOk(await api.clear())
    },
  }
}
