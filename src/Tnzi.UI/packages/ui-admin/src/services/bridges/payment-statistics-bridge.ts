/**
 * Payment statistics bridge - thin adapter over `@tnzi/core`'s admin
 * payment-statistics API (`useAdminPaymentStatisticsApi`, wrapping
 * `/admin/payment-statistics/*` exposed by
 * `Tnzi.Payment.Controllers.Admin.DefaultPaymentStatisticsAdminController`).
 *
 * Delegates to the canonical `useAdminPaymentStatisticsApi` factory in
 * `@tnzi/core/services/payment`:
 *   • getOverview            → getStatistics({ startTime, endTime })
 *   • getRevenueTrend        → getRevenueTrend({ startTime, endTime, granularity })
 *   • getSubscriptionMetrics → getSubscriptionMetrics()
 *
 * This file keeps the bridge's original public surface (DTO re-exports +
 * `createPaymentStatisticsBridge`) so consuming pages are unaffected.
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminPaymentStatisticsApi,
  type ChannelStatisticsDto as CoreChannelStatisticsDto,
  type PaymentStatisticsDto as CorePaymentStatisticsDto,
  type RevenueTrendPointDto as CoreRevenueTrendPointDto,
  type PlanDistributionDto as CorePlanDistributionDto,
  type SubscriptionMetricsDto as CoreSubscriptionMetricsDto,
  type TrendGranularity as CoreTrendGranularity,
} from '@tnzi/core/services/payment'
import { unwrapResult as unwrap } from '../_mappers'

// Re-export under the original bridge names consumed by pages.
export type TrendGranularity = CoreTrendGranularity
export type ChannelStatisticsDto = CoreChannelStatisticsDto
export type PaymentStatisticsDto = CorePaymentStatisticsDto
export type RevenueTrendPointDto = CoreRevenueTrendPointDto
export type PlanDistributionDto = CorePlanDistributionDto
export type SubscriptionMetricsDto = CoreSubscriptionMetricsDto

export interface PaymentStatisticsBridgeDeps {
  client?: HttpClient
}

export interface PaymentStatisticsBridge {
  getOverview(startTime?: string, endTime?: string): Promise<PaymentStatisticsDto | null>
  getRevenueTrend(startTime: string, endTime: string, granularity: TrendGranularity): Promise<RevenueTrendPointDto[]>
  getSubscriptionMetrics(): Promise<SubscriptionMetricsDto | null>
}

export function createPaymentStatisticsBridge(deps: PaymentStatisticsBridgeDeps = {}): PaymentStatisticsBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createPaymentStatisticsBridge: no HttpClient provided'))
    return {
      getOverview: noOp as never,
      getRevenueTrend: noOp as never,
      getSubscriptionMetrics: noOp as never,
    }
  }

  const api = useAdminPaymentStatisticsApi(client)

  return {
    getOverview: async (startTime?: string, endTime?: string) =>
      unwrap<PaymentStatisticsDto | null>(await api.getStatistics({ startTime, endTime })),
    getRevenueTrend: async (startTime: string, endTime: string, granularity: TrendGranularity) =>
      unwrap<RevenueTrendPointDto[]>(await api.getRevenueTrend({ startTime, endTime, granularity })) ?? [],
    getSubscriptionMetrics: async () =>
      unwrap<SubscriptionMetricsDto | null>(await api.getSubscriptionMetrics()),
  }
}
