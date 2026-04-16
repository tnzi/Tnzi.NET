import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../src/services/bridges/payment-bridge', () => ({
  createPaymentBridge: () => ({
    orders: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => { throw new Error('backend gap') }),
      statistics: vi.fn(async () => ({
        totalRevenue: 0, totalTransactions: 0, successfulTransactions: 0,
        failedTransactions: 0, totalRefunds: 0, refundCount: 0, refundRate: 0,
        activeSubscriptions: 0, channelDistribution: [], startTime: '', endTime: '',
      })),
    },
    subscriptions: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 's1',
            subscriptionNo: 'SUB-001',
            userId: 'u1',
            planId: 'plan-1',
            planName: 'Basic',
            status: 1,
            cycleType: 1,
            cycleValue: 1,
            startTime: '2026-01-01T00:00:00Z',
            nextBillingTime: '2026-02-01T00:00:00Z',
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
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => ({ id: 's1' })),
      delete: vi.fn(async () => { throw new Error('backend gap') }),
    },
    refunds: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      approve: vi.fn(),
      reject: vi.fn(),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: {} }),
}))

const stubs = {
  DataTable:   { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination:  { template: '<div />' },
  Input:       { props: ['value'], template: '<input />' },
  Button:      { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal:       { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover:     { template: '<div><slot name="trigger" /></div>' },
  Checkbox:    { template: '<input type="checkbox" />' },
  Form:        { template: '<form><slot /></form>' },
  FormItem:    { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch:      { template: '<button />' },
  Select:      { template: '<select />' },
  DatePicker:  { template: '<input type="date" />' },
}

describe('PaymentSubscription page (Phase 3.33)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: PaymentSubscription } = await import('../../src/pages/payment/PaymentSubscription.vue')
    const wrapper = mount(PaymentSubscription, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders data table after fetch', async () => {
    const { default: PaymentSubscription } = await import('../../src/pages/payment/PaymentSubscription.vue')
    const wrapper = mount(PaymentSubscription, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('paymentSubscriptionColumns config', () => {
  it('exports columns with required keys', async () => {
    const { paymentSubscriptionColumns } = await import('../../src/pages/payment/payment-subscription-config')
    const keys = paymentSubscriptionColumns.map((c) => c.key)
    expect(keys).toContain('subscriptionNo')
    expect(keys).toContain('userId')
    expect(keys).toContain('planName')
    expect(keys).toContain('status')
    expect(keys).toContain('cycleType')
    expect(keys).toContain('nextBillingTime')
  })

  it('exports formSchema with required fields', async () => {
    const { paymentSubscriptionFormSchema } = await import('../../src/pages/payment/payment-subscription-config')
    const keys = paymentSubscriptionFormSchema.map((f) => f.key)
    expect(keys).toContain('userId')
    expect(keys).toContain('planId')
    expect(keys).toContain('cycleType')
  })
})
