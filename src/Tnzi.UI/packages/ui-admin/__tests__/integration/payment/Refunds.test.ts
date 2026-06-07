import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const mockApproveFn = vi.fn(async () => undefined)
const mockRejectFn = vi.fn(async () => undefined)

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/payment-bridge', () => ({
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
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
    refunds: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'r1',
            refundNo: 'REF-001',
            paymentId: 'p1',
            paymentNo: 'PAY-001',
            amount: 50,
            reason: 'Customer request',
            status: 0,
            creationTime: '2026-01-02T00:00:00Z',
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
      approve: mockApproveFn,
      reject: mockRejectFn,
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

describe('Refunds page (Phase 3.34)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without throwing', async () => {
    const { default: Refunds } = await import('../../../src/pages/payment/Refunds.vue')
    const wrapper = mount(Refunds, { global: { stubs } })
    await nextTick()
    expect(wrapper.find('.t-crud-page').exists()).toBe(true)
  })

  it('renders data table after fetch', async () => {
    const { default: Refunds } = await import('../../../src/pages/payment/Refunds.vue')
    const wrapper = mount(Refunds, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })
})

describe('refund-config', () => {
  it('exports columns with required keys', async () => {
    const { refundColumns } = await import('../../../src/pages/payment/refund-config')
    const keys = refundColumns.map((c) => c.key)
    expect(keys).toContain('refundNo')
    expect(keys).toContain('paymentNo')
    expect(keys).toContain('refundAmount')
    expect(keys).toContain('reason')
    expect(keys).toContain('status')
  })

  it('exports formSchema with required fields', async () => {
    const { refundFormSchema } = await import('../../../src/pages/payment/refund-config')
    const keys = refundFormSchema.map((f) => f.key)
    expect(keys).toContain('refundNo')
    expect(keys).toContain('refundAmount')
    expect(keys).toContain('reason')
    expect(keys).toContain('status')
  })
})
