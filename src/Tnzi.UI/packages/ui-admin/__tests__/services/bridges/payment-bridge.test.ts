import { describe, it, expect, vi } from 'vitest'
import { createPaymentBridge } from '../../../src/services/bridges/payment-bridge'

// ---------------------------------------------------------------------------
// Mock factory helpers
// ---------------------------------------------------------------------------

function mockAdminPaymentApi() {
  return {
    getList: vi.fn(async () => ({
      items: [
        {
          id: 'p1',
          paymentNo: 'PAY-001',
          tradeNo: 'TRADE-001',
          businessOrderNo: 'ORD-001',
          businessType: 1,
          amount: 100,
          currency: 'CNY',
          status: 1,
          userId: 'u1',
          discountAmount: 0,
          finalAmount: 100,
          refundAmount: 0,
          creationTime: '2026-01-01T00:00:00Z',
          lastModificationTime: null,
        },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    get: vi.fn(async () => ({ id: 'p1', paymentNo: 'PAY-001' })),
    close: vi.fn(async () => undefined),
    sync: vi.fn(async () => undefined),
  }
}

function mockAdminRefundApi() {
  return {
    getList: vi.fn(async () => ({
      items: [
        {
          id: 'r1',
          refundNo: 'REF-001',
          tradeNo: 'TRADE-001',
          refundAmount: 50,
          currency: 'USD',
          reason: 'Customer request',
          status: 'Pending',
          creationTime: '2026-01-02T00:00:00Z',
        },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    get: vi.fn(async () => ({ id: 'r1', refundNo: 'REF-001' })),
    getByTradeNo: vi.fn(async () => []),
    approve: vi.fn(async () => undefined),
    process: vi.fn(async () => undefined),
    cancel: vi.fn(async () => undefined),
  }
}

function mockAdminSubscriptionApi() {
  return {
    getList: vi.fn(async () => ({
      items: [
        {
          id: 's1',
          subscriptionNo: 'SUB-001',
          userId: 'u1',
          planId: 'plan-1',
          status: 1,
          cycleType: 1,
          cycleValue: 1,
          startTime: '2026-01-01T00:00:00Z',
          originalPrice: 99,
          paidAmount: 99,
          discountAmount: 0,
          currency: 'CNY',
          autoRenew: true,
          creationTime: '2026-01-01T00:00:00Z',
        },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    get: vi.fn(async () => ({ id: 's1' })),
    cancel: vi.fn(async () => undefined),
    getPlans: vi.fn(async () => []),
    createPlan: vi.fn(async () => ({})),
    updatePlan: vi.fn(async () => undefined),
    deletePlan: vi.fn(async () => undefined),
  }
}

function mockAdminStatisticsApi() {
  return {
    getStatistics: vi.fn(async () => ({
      startTime: '2026-01-01T00:00:00Z',
      endTime: '2026-01-31T23:59:59Z',
      totalRevenue: 10000,
      totalTransactions: 50,
      successfulTransactions: 45,
      failedTransactions: 5,
      totalRefunds: 500,
      refundCount: 3,
      refundRate: 0.06,
      activeSubscriptions: 20,
      channelDistribution: [],
    })),
    getRevenueTrend: vi.fn(async () => []),
    getSubscriptionMetrics: vi.fn(async () => ({})),
    getPromotionAnalytics: vi.fn(async () => []),
    getRefundAnalytics: vi.fn(async () => ({})),
    exportReconciliation: vi.fn(async () => new Blob()),
    getReconciliationSummary: vi.fn(async () => ({})),
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('payment-bridge', () => {
  it('exposes orders / subscriptions / refunds sub-contracts', () => {
    const bridge = createPaymentBridge({
      adminPaymentApi: mockAdminPaymentApi() as never,
      adminRefundApi: mockAdminRefundApi() as never,
      adminSubscriptionApi: mockAdminSubscriptionApi() as never,
      adminStatisticsApi: mockAdminStatisticsApi() as never,
    })
    expect(typeof bridge.orders.fetch).toBe('function')
    expect(typeof bridge.subscriptions.fetch).toBe('function')
    expect(typeof bridge.refunds.fetch).toBe('function')
  })

  // ---- orders sub-contract ----

  it('orders.fetch calls adminPaymentApi.getList and returns paged items', async () => {
    const adminPaymentApi = mockAdminPaymentApi()
    const bridge = createPaymentBridge({
      adminPaymentApi: adminPaymentApi as never,
      adminRefundApi: mockAdminRefundApi() as never,
      adminSubscriptionApi: mockAdminSubscriptionApi() as never,
      adminStatisticsApi: mockAdminStatisticsApi() as never,
    })
    const result = await bridge.orders.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(adminPaymentApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('orders.statistics returns statistics dto', async () => {
    const adminStatisticsApi = mockAdminStatisticsApi()
    const bridge = createPaymentBridge({
      adminPaymentApi: mockAdminPaymentApi() as never,
      adminRefundApi: mockAdminRefundApi() as never,
      adminSubscriptionApi: mockAdminSubscriptionApi() as never,
      adminStatisticsApi: adminStatisticsApi as never,
    })
    const stats = await bridge.orders.statistics()
    expect(adminStatisticsApi.getStatistics).toHaveBeenCalled()
    expect(stats.totalRevenue).toBe(10000)
    expect(stats.totalTransactions).toBe(50)
  })

  // orders/subscriptions/refunds are read-only ledgers — the contracts expose
  // ONLY fetch + real lifecycle endpoints (no create/update/delete stubs).

  // ---- subscriptions sub-contract ----

  it('subscriptions.fetch calls adminSubscriptionApi.getList and returns paged items', async () => {
    const adminSubscriptionApi = mockAdminSubscriptionApi()
    const bridge = createPaymentBridge({
      adminPaymentApi: mockAdminPaymentApi() as never,
      adminRefundApi: mockAdminRefundApi() as never,
      adminSubscriptionApi: adminSubscriptionApi as never,
      adminStatisticsApi: mockAdminStatisticsApi() as never,
    })
    const result = await bridge.subscriptions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(adminSubscriptionApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('subscriptions.cancelAtPeriodEnd calls adminSubscriptionApi.cancel with immediate=false', async () => {
    const adminSubscriptionApi = mockAdminSubscriptionApi()
    const bridge = createPaymentBridge({
      adminPaymentApi: mockAdminPaymentApi() as never,
      adminRefundApi: mockAdminRefundApi() as never,
      adminSubscriptionApi: adminSubscriptionApi as never,
      adminStatisticsApi: mockAdminStatisticsApi() as never,
    })
    await bridge.subscriptions.cancelAtPeriodEnd('s1')
    expect(adminSubscriptionApi.cancel).toHaveBeenCalledWith('s1', expect.objectContaining({ immediate: false }))
  })

  // ---- refunds sub-contract ----

  it('refunds.fetch calls adminRefundApi.getList and returns paged items', async () => {
    const adminRefundApi = mockAdminRefundApi()
    const bridge = createPaymentBridge({
      adminPaymentApi: mockAdminPaymentApi() as never,
      adminRefundApi: adminRefundApi as never,
      adminSubscriptionApi: mockAdminSubscriptionApi() as never,
      adminStatisticsApi: mockAdminStatisticsApi() as never,
    })
    const result = await bridge.refunds.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(adminRefundApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('refunds.approve calls adminRefundApi.approve with approved=true', async () => {
    const adminRefundApi = mockAdminRefundApi()
    const bridge = createPaymentBridge({
      adminPaymentApi: mockAdminPaymentApi() as never,
      adminRefundApi: adminRefundApi as never,
      adminSubscriptionApi: mockAdminSubscriptionApi() as never,
      adminStatisticsApi: mockAdminStatisticsApi() as never,
    })
    await bridge.refunds.approve('r1')
    expect(adminRefundApi.approve).toHaveBeenCalledWith('r1', { approved: true })
  })

  it('refunds.reject calls adminRefundApi.approve with approved=false and remark', async () => {
    const adminRefundApi = mockAdminRefundApi()
    const bridge = createPaymentBridge({
      adminPaymentApi: mockAdminPaymentApi() as never,
      adminRefundApi: adminRefundApi as never,
      adminSubscriptionApi: mockAdminSubscriptionApi() as never,
      adminStatisticsApi: mockAdminStatisticsApi() as never,
    })
    await bridge.refunds.reject('r1', 'Fraud detected')
    expect(adminRefundApi.approve).toHaveBeenCalledWith('r1', { approved: false, remark: 'Fraud detected' })
  })

  // ---- no-deps fallback ----

  it('bridge with no deps returns stub contracts that reject on call', async () => {
    const bridge = createPaymentBridge()
    await expect(bridge.orders.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
    await expect(bridge.refunds.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
    await expect(bridge.subscriptions.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
  })
})
