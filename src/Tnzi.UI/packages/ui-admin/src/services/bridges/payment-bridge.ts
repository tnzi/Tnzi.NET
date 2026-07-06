/**
 * Payment bridge — adapts the payment backend API to the shapes consumed by
 * the TCrudPage-based payment pages.
 *
 * Every payment record surfaced here is an immutable financial ledger entry:
 * the admin pages are read-only lists plus lifecycle actions, never
 * create/update/delete. The contracts therefore expose ONLY `fetch` plus the
 * real lifecycle endpoints — there is no admin create/update/delete endpoint
 * for payments, subscriptions or refunds, so no stubbed CRUD methods are
 * carried.
 *
 * Sub-contracts:
 *   - orders        → useAdminPaymentApi (query) + useAdminPaymentStatisticsApi (overview)
 *   - subscriptions → useAdminSubscriptionApi (query, cancel-at-period-end)
 *   - refunds       → useAdminRefundApi (query, approve, reject)
 */
import {
  useAdminPaymentApi,
  useAdminRefundApi,
  useAdminSubscriptionApi,
  useAdminPaymentStatisticsApi,
  type PaymentDto,
  type RefundDto,
  type SubscriptionDto,
  type PaymentStatisticsDto,
  type StatisticsQueryDto,
  type PaymentQueryDto,
  type RefundQueryDto,
  type SubscriptionQueryDto,
} from '@tnzi/core/services/payment'
import type { CrudPageQuery, CrudPageResult } from '../types'
import { mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminPaymentApi>[0]

export interface PaymentBridgeDeps {
  /** Production path: provide HttpClient; bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly. */
  adminPaymentApi?: ReturnType<typeof useAdminPaymentApi>
  adminRefundApi?: ReturnType<typeof useAdminRefundApi>
  adminSubscriptionApi?: ReturnType<typeof useAdminSubscriptionApi>
  adminStatisticsApi?: ReturnType<typeof useAdminPaymentStatisticsApi>
}

/** orders sub-contract: read-only list + statistics overview. */
export interface PaymentOrderContract {
  fetch(query: CrudPageQuery): Promise<CrudPageResult<PaymentDto>>
  /**
   * Fetch payment statistics overview for the Order page KPI strip.
   * Maps to GET /admin/payment-statistics.
   */
  statistics(query?: StatisticsQueryDto): Promise<PaymentStatisticsDto>
}

/** subscriptions sub-contract: read-only list + cancel-at-period-end. */
export interface PaymentSubscriptionContract {
  fetch(query: CrudPageQuery): Promise<CrudPageResult<SubscriptionDto>>
  /**
   * Cancel subscription at end of current billing period (immediate=false).
   * There is NO admin subscription update endpoint — admins may only cancel.
   */
  cancelAtPeriodEnd(id: string): Promise<void>
}

/** refunds sub-contract: read-only list + approve/reject. */
export interface PaymentRefundContract {
  fetch(query: CrudPageQuery): Promise<CrudPageResult<RefundDto>>
  /** Approve a pending refund. Maps to POST /admin/refunds/{id}/approve with approved=true. */
  approve(id: string): Promise<void>
  /** Reject a pending refund with a reason. Maps to POST /admin/refunds/{id}/approve with approved=false. */
  reject(id: string, reason: string): Promise<void>
}

export interface PaymentBridge {
  orders: PaymentOrderContract
  subscriptions: PaymentSubscriptionContract
  refunds: PaymentRefundContract
}

const backendGapReject = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`payment-bridge: ${name} — no HttpClient / API provided`))

export function createPaymentBridge(deps: PaymentBridgeDeps = {}): PaymentBridge {
  const paymentApi = deps.adminPaymentApi ?? (deps.client ? useAdminPaymentApi(deps.client) : null)
  const refundApi = deps.adminRefundApi ?? (deps.client ? useAdminRefundApi(deps.client) : null)
  const subscriptionApi = deps.adminSubscriptionApi ?? (deps.client ? useAdminSubscriptionApi(deps.client) : null)
  const statisticsApi = deps.adminStatisticsApi ?? (deps.client ? useAdminPaymentStatisticsApi(deps.client) : null)

  if (!paymentApi || !refundApi || !subscriptionApi || !statisticsApi) {
    const noFetch = backendGapReject('no deps provided')
    return {
      orders: {
        fetch: noFetch as never,
        statistics: backendGapReject('orders.statistics'),
      },
      subscriptions: {
        fetch: noFetch as never,
        cancelAtPeriodEnd: backendGapReject('subscriptions.cancelAtPeriodEnd'),
      },
      refunds: {
        fetch: noFetch as never,
        approve: backendGapReject('refunds.approve'),
        reject: backendGapReject('refunds.reject'),
      },
    }
  }

  // Narrowed references for closures
  const pa = paymentApi
  const ra = refundApi
  const sa = subscriptionApi
  const sta = statisticsApi

  async function fetchOrders(query: CrudPageQuery): Promise<CrudPageResult<PaymentDto>> {
    const params = mapQueryToListRequest(query) as unknown as PaymentQueryDto
    const result = unwrap<{ items: PaymentDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await pa.getList(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const orders: PaymentOrderContract = {
    fetch: fetchOrders,
    statistics: async (query?: StatisticsQueryDto): Promise<PaymentStatisticsDto> => {
      return unwrap<PaymentStatisticsDto>(await sta.getStatistics(query))
    },
  }

  async function fetchSubscriptions(query: CrudPageQuery): Promise<CrudPageResult<SubscriptionDto>> {
    const params = mapQueryToListRequest(query) as unknown as SubscriptionQueryDto
    const result = unwrap<{ items: SubscriptionDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await sa.getList(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const subscriptions: PaymentSubscriptionContract = {
    fetch: fetchSubscriptions,
    cancelAtPeriodEnd: async (id: string): Promise<void> => {
      await sa.cancel(id, { reason: undefined, immediate: false })
    },
  }

  async function fetchRefunds(query: CrudPageQuery): Promise<CrudPageResult<RefundDto>> {
    const params = mapQueryToListRequest(query) as unknown as RefundQueryDto
    const result = unwrap<{ items: RefundDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
      await ra.getList(params),
    )
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const refunds: PaymentRefundContract = {
    fetch: fetchRefunds,
    approve: async (id: string): Promise<void> => {
      await ra.approve(id, { approved: true })
    },
    reject: async (id: string, reason: string): Promise<void> => {
      await ra.approve(id, { approved: false, remark: reason })
    },
  }

  return { orders, subscriptions, refunds }
}
