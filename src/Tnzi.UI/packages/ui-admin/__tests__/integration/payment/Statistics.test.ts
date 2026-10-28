import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

// Rates arrive as percentages on a 0-100 scale: the service computes
// `refundCount / totalTransactions * 100` and `cancelled / lastMonthActive * 100`.
// 3 refunds over 50 transactions is 6.00; 1 cancellation over 40 is 2.50.
const overview = {
  startTime: '2026-01-01T00:00:00Z',
  endTime: '2026-01-31T23:59:59Z',
  totalRevenue: 10000,
  totalTransactions: 50,
  successfulTransactions: 45,
  failedTransactions: 5,
  totalRefunds: 500,
  refundCount: 3,
  refundRate: 6,
  activeSubscriptions: 40,
  channelDistribution: [{ channelCode: 'stripe', revenue: 10000, transactionCount: 50, percentage: 100 }],
}
const metrics = {
  monthlyRecurringRevenue: 1200,
  activeSubscriptions: 40,
  trialSubscriptions: 2,
  newSubscriptionsThisMonth: 3,
  cancelledThisMonth: 1,
  churnRate: 2.5,
  averageRevenuePerUser: 30,
  planDistribution: [],
}

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/payment-statistics-bridge', () => ({
  createPaymentStatisticsBridge: () => ({
    getOverview: vi.fn(async () => overview),
    getRevenueTrend: vi.fn(async () => []),
    getSubscriptionMetrics: vi.fn(async () => metrics),
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Select: { template: '<select />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  TWidgetLineChart: { template: '<div class="chart" />' },
}

describe('Payment statistics page', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  // A rate is shown as the percentage the server computed, not scaled again:
  // 6 refunds per 100 transactions read as 600.00% when the page multiplied by 100.
  it('shows the refund and churn rates as the percentages the server returns', async () => {
    const { default: Statistics } = await import('../../../src/pages/payment/Statistics.vue')
    const wrapper = mount(Statistics, { global: { stubs } })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    const text = wrapper.text()
    expect(text).toContain('6.00%')
    expect(text).toContain('2.50%')
    expect(text).not.toContain('600.00%')
    expect(text).not.toContain('250.00%')
  })
})
