import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * The platform operator's view of an account's payments: each order shows the
 * billing account it was paid for, and a paid order can be refunded from the
 * list (amount + reason, `POST admin/refunds`) by holders of
 * `payment.refund.create`.
 */

const createRefund = vi.fn(async () => ({ id: 'r1' }))
const fetchOrders = vi.fn(async () => ({
  items: [
    {
      id: 'p-paid',
      tradeNo: 'TRADE-PAID',
      businessOrderNo: 'SUB-1',
      businessType: 'Subscription',
      originalAmount: 300,
      paidAmount: 300,
      discountAmount: 0,
      currency: 'CAD',
      status: 'Succeeded',
      channelCode: 'Stripe',
      paymentMethod: 'CreditCard',
      paidTime: '2026-10-01T00:00:00Z',
      creationTime: '2026-10-01T00:00:00Z',
      billingAccountType: 'Installation',
      billingAccountId: 'default',
    },
    {
      id: 'p-open',
      tradeNo: 'TRADE-OPEN',
      businessOrderNo: 'SUB-2',
      businessType: 'Subscription',
      originalAmount: 300,
      paidAmount: 0,
      discountAmount: 0,
      currency: 'CAD',
      status: 'Processing',
      channelCode: 'Stripe',
      paymentMethod: 'CreditCard',
      paidTime: null,
      creationTime: '2026-10-02T00:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/payment-bridge', () => ({
  createPaymentBridge: () => ({
    orders: { fetch: fetchOrders, statistics: vi.fn(async () => null) },
    refunds: { fetch: vi.fn(), create: createRefund },
  }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useRoute: () => ({ query: {} }),
}))

let wrapper: VueWrapper | null = null

async function mountOrders(permissions: string[]): Promise<VueWrapper> {
  const { useAdminAuthStore } = await import('../../../src/stores/useAdminAuthStore')
  useAdminAuthStore().setUserInfo({ id: 'ops', username: 'ops', roles: [], permissions } as never)
  const { default: Orders } = await import('../../../src/pages/payment/Orders.vue')
  wrapper = mount(Orders, { attachTo: document.body })
  await flushPromises()
  await new Promise((r) => setTimeout(r, 20))
  return wrapper
}

describe('Orders: billing account and refunds', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    createRefund.mockClear()
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('shows the billing account an order was paid for', async () => {
    const page = await mountOrders(['payment.order.view'])

    const accounts = page.findAll('[data-testid="order-billing-account"]').map((n) => n.text())
    expect(accounts).toEqual(['Installation:default'])
  })

  it('offers Refund on paid orders only, and only to holders of payment.refund.create', async () => {
    const withCode = await mountOrders(['payment.order.view', 'payment.refund.create'])
    const cards = withCode.findAll('.t-item-card')
    expect(cards).toHaveLength(2)
    expect(cards[0]!.findComponent({ name: 'TRowActions' }).exists()).toBe(true)
    expect(cards[1]!.findComponent({ name: 'TRowActions' }).exists()).toBe(false)
    withCode.unmount()
    wrapper = null

    setActivePinia(createPinia())
    const withoutCode = await mountOrders(['payment.order.view'])
    expect(withoutCode.findComponent({ name: 'TRowActions' }).exists()).toBe(false)
  })

  it('raises the refund with the amount and reason entered', async () => {
    const page = await mountOrders(['payment.order.view', 'payment.refund.create'])

    const actions = page.findComponent({ name: 'TRowActions' })
    const refund = (actions.props('actions') as Array<{ key: string; onClick: (row: unknown) => void }>).find((a) => a.key === 'refund')!
    refund.onClick(actions.props('row'))
    await flushPromises()

    // The modal is teleported; its components are still in this page's tree
    const reason = page.findAllComponents({ name: 'Input' }).find((c) => c.attributes('data-testid') === 'refund-reason')
    expect(reason).toBeDefined()
    reason!.vm.$emit('update:value', 'Service credit for the outage')
    await flushPromises()

    const submit = document.querySelector<HTMLButtonElement>('[data-testid="refund-submit"]')
    expect(submit).not.toBeNull()
    submit!.click()
    await flushPromises()

    expect(createRefund).toHaveBeenCalledWith('TRADE-PAID', 300, 'Service credit for the outage')
  })
})

describe('billing-account-config', () => {
  it('shows the account as type:id, and a placeholder when it is unknown', async () => {
    const { formatBillingAccount } = await import('../../../src/pages/payment/billing-account-config')
    expect(formatBillingAccount({ billingAccountType: 'Tenant', billingAccountId: 'abc' })).toBe('Tenant:abc')
    expect(formatBillingAccount({})).not.toContain(':')
  })

  it('searches on the two filters the admin list endpoints take', async () => {
    const { billingAccountSearchFields } = await import('../../../src/pages/payment/billing-account-config')
    expect(billingAccountSearchFields().map((f) => f.key)).toEqual(['billingAccountType', 'billingAccountId'])
  })
})
