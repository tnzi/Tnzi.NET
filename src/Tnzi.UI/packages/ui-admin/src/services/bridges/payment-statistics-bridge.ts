/**
 * Payment statistics bridge — wraps `/admin/payment-statistics/*` exposed
 * by `Tnzi.Payment.Controllers.Admin.DefaultPaymentStatisticsAdminController`.
 *
 * 7 endpoints (overview / revenue trend / subscription metrics / promotion
 * analytics / refund analytics / reconciliation summary + export).
 */
import type { HttpClient } from '@tnzi/core/http'

export type TrendGranularity = 1 | 2 | 3 // Day / Week / Month

export interface ChannelStatisticsDto {
  channelCode: string
  revenue: number
  transactionCount: number
  percentage: number
}

export interface PaymentStatisticsDto {
  startTime: string
  endTime: string
  totalRevenue: number
  totalTransactions: number
  successfulTransactions: number
  failedTransactions: number
  totalRefunds: number
  refundCount: number
  refundRate: number
  activeSubscriptions: number
  channelDistribution: ChannelStatisticsDto[]
}

export interface RevenueTrendPointDto {
  date: string
  revenue: number
  transactionCount: number
  refundAmount: number
  netRevenue: number
}

export interface PlanDistributionDto {
  planCode: string
  planName: string
  subscriberCount: number
  monthlyRevenue: number
}

export interface SubscriptionMetricsDto {
  monthlyRecurringRevenue: number
  activeSubscriptions: number
  trialSubscriptions: number
  newSubscriptionsThisMonth: number
  cancelledThisMonth: number
  churnRate: number
  averageRevenuePerUser: number
  planDistribution: PlanDistributionDto[]
}

export interface PaymentStatisticsBridgeDeps {
  client?: HttpClient
}

export interface PaymentStatisticsBridge {
  getOverview(startTime?: string, endTime?: string): Promise<PaymentStatisticsDto | null>
  getRevenueTrend(startTime: string, endTime: string, granularity: TrendGranularity): Promise<RevenueTrendPointDto[]>
  getSubscriptionMetrics(): Promise<SubscriptionMetricsDto | null>
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
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

  return {
    getOverview: async (startTime?: string, endTime?: string) => {
      const params = new URLSearchParams()
      if (startTime) params.set('startTime', startTime)
      if (endTime) params.set('endTime', endTime)
      const qs = params.toString()
      return unwrap<PaymentStatisticsDto | null>(
        await client.get<PaymentStatisticsDto>(`/admin/payment-statistics${qs ? `?${qs}` : ''}`),
      )
    },
    getRevenueTrend: async (startTime: string, endTime: string, granularity: TrendGranularity) => {
      const params = new URLSearchParams({
        startTime,
        endTime,
        granularity: String(granularity),
      })
      return (
        unwrap<RevenueTrendPointDto[]>(
          await client.get<RevenueTrendPointDto[]>(
            `/admin/payment-statistics/revenue-trend?${params.toString()}`,
          ),
        ) ?? []
      )
    },
    getSubscriptionMetrics: async () =>
      unwrap<SubscriptionMetricsDto | null>(
        await client.get<SubscriptionMetricsDto>('/admin/payment-statistics/subscription-metrics'),
      ),
  }
}
