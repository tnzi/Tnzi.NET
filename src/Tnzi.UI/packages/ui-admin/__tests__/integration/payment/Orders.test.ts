import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const mockStats = {
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
}

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/payment-bridge', () => ({
  createPaymentBridge: () => ({
    orders: {
      fetch: vi.fn(async () => ({
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
      create: vi.fn(async () => { throw new Error('backend gap') }),
      update: vi.fn(async () => { throw new Error('backend gap') }),
      delete: vi.fn(async () => { throw new Error('backend gap') }),
      statistics: vi.fn(async () => mockStats),
    },
    subscriptions: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
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
  Card:        { template: '<div class="n-card"><slot /><slot name="header" /></div>' },
  Statistic:   { props: ['value', 'label'], template: '<div class="n-statistic" :data-value="value" :data-label="label" />' },
  Grid:        { props: ['cols'], template: '<div class="n-grid"><slot /></div>' },
  GridItem:    { template: '<div class="n-grid-item"><slot /></div>' },
}

describe('Orders page (Phase 3.32)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: Orders } = await import('../../../src/pages/payment/Orders.vue')
    const wrapper = mount(Orders, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders data table after fetch', async () => {
    const { default: Orders } = await import('../../../src/pages/payment/Orders.vue')
    const wrapper = mount(Orders, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })

  it('renders statistics banner with stats cards', async () => {
    const { default: Orders } = await import('../../../src/pages/payment/Orders.vue')
    const wrapper = mount(Orders, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    expect(wrapper.find('.orders-page__stats').exists()).toBe(true)
  })
})

describe('order-config', () => {
  it('exports columns with required keys', async () => {
    const { orderColumns } = await import('../../../src/pages/payment/order-config')
    const keys = orderColumns.map((c) => c.key)
    expect(keys).toContain('paymentNo')
    expect(keys).toContain('businessOrderNo')
    expect(keys).toContain('userId')
    expect(keys).toContain('amount')
    expect(keys).toContain('status')
  })

  it('exports formSchema with required fields', async () => {
    const { orderFormSchema } = await import('../../../src/pages/payment/order-config')
    const keys = orderFormSchema.map((f) => f.key)
    expect(keys).toContain('paymentNo')
    expect(keys).toContain('userId')
    expect(keys).toContain('amount')
    expect(keys).toContain('status')
  })
})
