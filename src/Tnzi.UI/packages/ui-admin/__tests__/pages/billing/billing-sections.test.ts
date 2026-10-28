/**
 * The customer-facing billing sections: the subscription with its cards, the
 * deprecated standalone card section, and billing history. The bridge is faked; the Stripe
 * card form is real, with `@stripe/stripe-js` mocked, so the pay-now test runs
 * the whole chain: pay-now -> Payment Element -> confirmPayment -> sync ->
 * standing refresh.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { resetStripeClientCache } from '@tnzi/core/services/payment'

const bridge = {
  getStanding: vi.fn(),
  getAccount: vi.fn(),
  updateAccount: vi.fn(),
  listSubscriptions: vi.fn(),
  payNow: vi.fn(),
  attachPaymentMethod: vi.fn(),
  syncPayment: vi.fn(),
  listPaymentMethods: vi.fn(),
  createSetupSession: vi.fn(),
  bindPaymentMethod: vi.fn(),
  setDefaultPaymentMethod: vi.fn(),
  removePaymentMethod: vi.fn(),
  getClientConfig: vi.fn(),
  listInvoices: vi.fn(),
  downloadInvoicePdf: vi.fn(),
  listPlans: vi.fn(),
  getStorageUsage: vi.fn(),
}

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))
vi.mock('../../../src/services/bridges/billing-bridge', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../src/services/bridges/billing-bridge')>()),
  createBillingBridge: () => bridge,
}))

const downloadBlob = vi.fn()
vi.mock('@tnzi/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/core')>()),
  downloadBlob: (...args: unknown[]) => downloadBlob(...args),
}))

const element = { mount: vi.fn(), destroy: vi.fn(), on: vi.fn((e: string, h: () => void) => e === 'ready' && h()) }
const stripe = {
  elements: vi.fn(() => ({ create: vi.fn(() => element), submit: vi.fn() })),
  confirmSetup: vi.fn(),
  confirmPayment: vi.fn(),
}
vi.mock('@stripe/stripe-js', () => ({ loadStripe: vi.fn(async () => stripe) }))

const stubs = {
  TModalShell: { props: ['show'], template: '<div v-if="show" class="modal"><slot /><slot name="footer" /></div>' },
  Popconfirm: { emits: ['positive-click'], template: '<span class="popconfirm" @click="$emit(\'positive-click\')"><slot name="trigger" /></span>' },
}

const pastDue = {
  id: 'sub-1',
  subscriptionNo: 'S-1',
  planName: 'Pro Monthly',
  status: 'PastDue',
  originalPrice: 99,
  currency: 'USD',
  nextBillingTime: '2026-11-01T00:00:00Z',
  hasPaymentMethod: true,
  paymentMethodBrand: 'visa',
  paymentMethodLast4: '4242',
}

function standing(canManage: boolean) {
  return { standing: 'Warning', canView: true, canManage, gateEnabled: true, isExempt: false, subscriptionId: 'sub-1' }
}

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i++) {
    await vi.dynamicImportSettled()
    await flushPromises()
  }
}

beforeEach(() => {
  setActivePinia(createPinia())
  resetStripeClientCache()
  vi.clearAllMocks()
  bridge.listSubscriptions.mockResolvedValue([pastDue])
  bridge.listPaymentMethods.mockResolvedValue([
    { id: 'pm-a', channelCode: 'Stripe', methodType: 'Card', brand: 'visa', last4: '4242', expiryMonth: 4, expiryYear: 2030, isDefault: true, isExpired: false, creationTime: '' },
    { id: 'pm-b', channelCode: 'Stripe', methodType: 'Card', brand: 'mastercard', last4: '5555', expiryMonth: 1, expiryYear: 2029, isDefault: false, isExpired: false, creationTime: '' },
  ])
  bridge.getClientConfig.mockResolvedValue({ channelCode: 'Stripe', publishableKey: 'pk_test' })
  bridge.getStorageUsage.mockResolvedValue({ metered: false, usageAvailable: true, sampleDays: 0, overageQuantity: 0, accruedOverageAmount: 0, inTrial: false })
})

const GB = 1073741824

function meteredUsage(usedGb: number, accrued: number, quantity: number) {
  return {
    metered: true,
    subscribed: true,
    usageAvailable: true,
    subscriptionId: 'sub-1',
    planName: 'Pro Monthly',
    includedBytes: 250 * GB,
    usedBytes: usedGb * GB,
    sampledOn: '2026-10-26T00:00:00Z',
    sampleDays: 12,
    averageBytes: usedGb * GB,
    overageQuantity: quantity,
    overageUnitPrice: 0.2,
    accruedOverageAmount: accrued,
    currency: 'CAD',
    inTrial: false,
  }
}

describe('BillingSubscriptionSection', () => {
  async function mountSection() {
    const { default: Section } = await import('../../../src/pages/billing/BillingSubscriptionSection.vue')
    const wrapper = mount(Section, { global: { stubs }, attachTo: document.body })
    await settle()
    return wrapper
  }

  it('shows plan, price, next charge and standing, and the cards on the same page', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    const wrapper = await mountSection()

    const facts = wrapper.find('[data-testid="billing-subscription-facts"]').text()
    expect(facts).toContain('Pro Monthly')
    expect(facts).toContain('$99.00')
    // The card is not a fact any more: the card block says it once
    expect(facts).not.toContain('4242')
    expect(wrapper.find('[data-testid="billing-card-on-file"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-cards"]').text()).toContain('mastercard **** 5555')
    expect(wrapper.find('[data-testid="billing-standing"]').text()).toContain('Payment overdue')
  })

  it('marks the card renewals are charged to on its row, with no separate renewal line', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.listSubscriptions.mockResolvedValue([{ ...pastDue, storedPaymentMethodId: 'pm-b', paymentMethodBrand: 'mastercard', paymentMethodLast4: '5555' }])
    const wrapper = await mountSection()

    const renewal = wrapper.find('[data-testid="billing-card-pm-b"]')
    expect(renewal.find('[data-testid="billing-card-renewal-tag"]').text()).toBe('Renewals')
    expect(renewal.find('[data-testid="billing-update-card"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-card-pm-a"] [data-testid="billing-card-renewal-tag"]').exists()).toBe(false)
    expect(wrapper.findAll('[data-testid="billing-update-card"]')).toHaveLength(1)
    expect(wrapper.find('[data-testid="billing-renewal-card"]').exists()).toBe(false)
  })

  it('says when renewals have no card at all', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.listSubscriptions.mockResolvedValue([{ ...pastDue, hasPaymentMethod: false, paymentMethodBrand: null, paymentMethodLast4: null }])
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-renewal-card"]').text()).toContain('Renewals have no card')
    expect(wrapper.find('[data-testid="billing-renewal-card"] [data-testid="billing-update-card"]').exists()).toBe(true)
  })

  it('a manager defaults and removes cards on the subscription page, and the subscription is read again', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.setDefaultPaymentMethod.mockResolvedValue(undefined)
    bridge.removePaymentMethod.mockResolvedValue(undefined)
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-add-card"]').exists()).toBe(true)
    const reads = bridge.listSubscriptions.mock.calls.length
    await wrapper.find('[data-testid="billing-default-pm-b"]').trigger('click')
    await settle()
    expect(bridge.setDefaultPaymentMethod).toHaveBeenCalledWith('pm-b')

    await wrapper.find('[data-testid="billing-remove-pm-a"]').trigger('click')
    await settle()
    expect(bridge.removePaymentMethod).toHaveBeenCalledWith('pm-a')
    // Removing or defaulting a card can change the card on the subscription
    expect(bridge.listSubscriptions.mock.calls.length).toBeGreaterThanOrEqual(reads + 2)
  })

  it('adds a card from the subscription page without attaching it to the subscription', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.createSetupSession.mockResolvedValue({ channelCode: 'Stripe', setupId: 'seti_1', clientSecret: 'seti_secret' })
    stripe.confirmSetup.mockResolvedValue({ setupIntent: { status: 'succeeded', payment_method: 'pm_new' } })
    bridge.bindPaymentMethod.mockResolvedValue({ id: 'stored-9' })
    const wrapper = await mountSection()

    await wrapper.find('[data-testid="billing-add-card"]').trigger('click')
    await settle()
    await wrapper.find('[data-testid="card-setup-save"]').trigger('click')
    await settle()

    expect(bridge.bindPaymentMethod).toHaveBeenCalled()
    expect(bridge.attachPaymentMethod).not.toHaveBeenCalled()
  })

  it('offers no card replacement while the first payment of a sign-up is still open', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.listSubscriptions.mockResolvedValue([{ ...pastDue, status: 'Pending' }])
    bridge.listPlans.mockResolvedValue([])
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-finish-payment"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-update-card"]').exists()).toBe(false)
  })

  it('hides pay and card actions when the server says the user cannot manage billing', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: false, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(false))
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-pay-now"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-update-card"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-add-card"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-remove-pm-a"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-cards"]').text()).toContain('visa **** 4242')
    // Said once for the whole page, not once per block
    expect(wrapper.findAll('[data-testid="billing-read-only"]')).toHaveLength(1)
  })

  it('follows canManage from the server even for a user holding no permission code', async () => {
    // User scope: no codes, but the server says the user manages their own account.
    bridge.getAccount.mockRejectedValue(new Error('404'))
    bridge.getStanding.mockResolvedValue(standing(true))
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-pay-now"]').exists()).toBe(true)
  })

  it('pays now through the Payment Element, syncs the payment and refreshes the standing', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.payNow.mockResolvedValue({
      subscriptionId: 'sub-1',
      tradeNo: 'T-1',
      payParams: 'pi_secret',
      amount: 99,
      taxAmount: 0,
      currency: 'USD',
      channelCode: 'Stripe',
      settledWithoutPayment: false,
    })
    stripe.confirmPayment.mockResolvedValue({ paymentIntent: { id: 'pi_1', status: 'succeeded' } })
    bridge.syncPayment.mockResolvedValue(undefined)
    const wrapper = await mountSection()
    const readsBefore = bridge.getStanding.mock.calls.length

    await wrapper.find('[data-testid="billing-pay-now"]').trigger('click')
    await settle()
    expect(bridge.payNow).toHaveBeenCalledWith('sub-1')
    expect(stripe.elements).toHaveBeenCalledWith({ clientSecret: 'pi_secret' })
    expect(wrapper.find('[data-testid="pay-now-amount"]').text()).toBe('$99.00')

    await wrapper.find('[data-testid="pay-now-confirm"]').trigger('click')
    await settle()

    expect(stripe.confirmPayment).toHaveBeenCalledWith(expect.objectContaining({ redirect: 'if_required' }))
    expect(bridge.syncPayment).toHaveBeenCalledWith('T-1')
    expect(bridge.getStanding.mock.calls.length).toBeGreaterThan(readsBefore)
    expect(wrapper.find('[data-testid="pay-now-confirm"]').exists()).toBe(false)
  })

  it('replaces the card: setup, confirm, bind as default, attach to the subscription', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.createSetupSession.mockResolvedValue({ channelCode: 'Stripe', setupId: 'seti_1', clientSecret: 'seti_secret' })
    stripe.confirmSetup.mockResolvedValue({ setupIntent: { status: 'succeeded', payment_method: 'pm_new' } })
    bridge.bindPaymentMethod.mockResolvedValue({ id: 'stored-9' })
    bridge.attachPaymentMethod.mockResolvedValue(pastDue)
    const wrapper = await mountSection()

    await wrapper.find('[data-testid="billing-update-card"]').trigger('click')
    await settle()
    await wrapper.find('[data-testid="card-setup-save"]').trigger('click')
    await settle()

    expect(bridge.bindPaymentMethod).toHaveBeenCalledWith('pm_new', 'Stripe', true)
    expect(bridge.attachPaymentMethod).toHaveBeenCalledWith('sub-1', 'stored-9')
  })

  it('shows storage used against the allowance and the overage accrued so far', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.getStorageUsage.mockResolvedValue(meteredUsage(300, 10, 50))
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-storage-used"]').text()).toBe('300 GB of 250 GB')
    expect(wrapper.find('[data-testid="billing-storage-overage"]').text()).toBe('CA$10.00 (50 GB-month)')
  })

  it('shows usage without an overage row while the account is within its allowance', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.getStorageUsage.mockResolvedValue(meteredUsage(120, 0, 0))
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-storage-used"]').text()).toBe('120 GB of 250 GB')
    expect(wrapper.find('[data-testid="billing-storage-overage"]').exists()).toBe(false)
  })

  it('shows no storage row when the plan does not meter storage, or the usage read fails', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    const unmetered = await mountSection()
    expect(unmetered.find('[data-testid="billing-storage"]').exists()).toBe(false)

    bridge.getStorageUsage.mockRejectedValue(new Error('Forbidden'))
    const failed = await mountSection()
    expect(failed.find('[data-testid="billing-storage"]').exists()).toBe(false)
    expect(failed.find('[data-testid="billing-subscription-facts"]').text()).toContain('Pro Monthly')
  })

  it('shows a failed subscription read instead of claiming there is none', async () => {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })
    bridge.getStanding.mockResolvedValue(standing(true))
    bridge.listSubscriptions.mockRejectedValue(new Error('Forbidden'))
    const wrapper = await mountSection()

    expect(wrapper.find('[data-testid="billing-subscription-error"]').text()).toContain('Forbidden')
  })
})

describe('BillingPaymentMethodsSection (deprecated standalone)', () => {
  async function mountSection(canManage: boolean) {
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage, billingEmails: [] })
    const { default: Section } = await import('../../../src/pages/billing/BillingPaymentMethodsSection.vue')
    const wrapper = mount(Section, { global: { stubs } })
    await settle()
    return wrapper
  }

  it('lists the cards for everyone and offers no actions without canManage', async () => {
    const wrapper = await mountSection(false)
    expect(wrapper.find('[data-testid="billing-cards"]').text()).toContain('visa **** 4242')
    expect(wrapper.find('[data-testid="billing-add-card"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-default-pm-b"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-remove-pm-a"]').exists()).toBe(false)
  })

  it('lets a manager make a card the default and remove one', async () => {
    bridge.setDefaultPaymentMethod.mockResolvedValue(undefined)
    bridge.removePaymentMethod.mockResolvedValue(undefined)
    const wrapper = await mountSection(true)

    expect(wrapper.find('[data-testid="billing-add-card"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-default-pm-a"]').exists()).toBe(false)
    await wrapper.find('[data-testid="billing-default-pm-b"]').trigger('click')
    await settle()
    expect(bridge.setDefaultPaymentMethod).toHaveBeenCalledWith('pm-b')

    await wrapper.find('[data-testid="billing-remove-pm-a"]').trigger('click')
    await settle()
    expect(bridge.removePaymentMethod).toHaveBeenCalledWith('pm-a')
  })
})

/**
 * The flows an app composes its own billing pages from, mounted through the public exports of
 * `@tnzi/ui-admin/billing` (the same components the default sections use).
 */
describe('billing flows for an app\'s own pages', () => {
  // An async component cannot be the root of a test mount; render it from a host, as an app's page does.
  function hostOf(component: unknown, props: Record<string, unknown>) {
    return mount(defineComponent({ setup: () => () => h(component as never, props) }), { global: { stubs } })
  }

  function cardSetupReady() {
    bridge.createSetupSession.mockResolvedValue({ channelCode: 'Stripe', setupId: 'seti_1', clientSecret: 'seti_secret' })
    stripe.confirmSetup.mockResolvedValue({ setupIntent: { status: 'succeeded', payment_method: 'pm_new' } })
    bridge.bindPaymentMethod.mockResolvedValue({ id: 'stored-9' })
  }

  it('the card setup dialog replaces a subscription\'s card and says so with saved + attached', async () => {
    cardSetupReady()
    bridge.attachPaymentMethod.mockResolvedValue({ ...pastDue, storedPaymentMethodId: 'stored-9' })
    const { TBillingCardSetupDialog } = await import('../../../src/billing')
    const wrapper = hostOf(TBillingCardSetupDialog, { show: true, subscriptionId: 'sub-1' })
    await settle()

    await wrapper.find('[data-testid="card-setup-save"]').trigger('click')
    await settle()

    expect(bridge.attachPaymentMethod).toHaveBeenCalledWith('sub-1', 'stored-9')
    const dialog = wrapper.findComponent({ name: 'BillingCardSetupModal' })
    expect(dialog.emitted('saved')?.[0]).toEqual([{ id: 'stored-9' }])
    expect(dialog.emitted('attached')?.[0]?.[0]).toMatchObject({ id: 'sub-1', storedPaymentMethodId: 'stored-9' })
    expect(dialog.emitted('update:show')?.at(-1)).toEqual([false])
    expect(dialog.emitted('cancel')).toBeUndefined()
  })

  it('without a subscription the card setup dialog only saves the card', async () => {
    cardSetupReady()
    const { TBillingCardSetupDialog } = await import('../../../src/billing')
    const wrapper = hostOf(TBillingCardSetupDialog, { show: true })
    await settle()

    await wrapper.find('[data-testid="card-setup-save"]').trigger('click')
    await settle()

    expect(bridge.attachPaymentMethod).not.toHaveBeenCalled()
    const dialog = wrapper.findComponent({ name: 'BillingCardSetupModal' })
    expect(dialog.emitted('saved')).toHaveLength(1)
    expect(dialog.emitted('attached')).toBeUndefined()
  })

  it('each dialog says cancel when it is closed without finishing', async () => {
    cardSetupReady()
    bridge.payNow.mockResolvedValue({ subscriptionId: 'sub-1', tradeNo: 'T-1', channelCode: 'Stripe', payParams: 'pi_secret', amount: 99, currency: 'USD' })
    const { TBillingCardSetupDialog, TBillingPayNowDialog, TBillingSubscribeDialog } = await import('../../../src/billing')
    for (const [component, props, name] of [
      [TBillingCardSetupDialog, { show: true }, 'BillingCardSetupModal'],
      [TBillingPayNowDialog, { show: true, subscriptionId: 'sub-1' }, 'BillingPayNowModal'],
      [TBillingSubscribeDialog, { show: true, plan: null }, 'BillingSubscribeModal'],
    ] as const) {
      const wrapper = hostOf(component, props)
      await settle()
      const cancel = wrapper.findAll('button').find((b) => b.text() === 'Cancel')
      expect(cancel, name).toBeTruthy()
      await cancel!.trigger('click')
      const dialog = wrapper.findComponent({ name })
      expect(dialog.emitted('cancel'), name).toHaveLength(1)
      expect(dialog.emitted('update:show')?.at(-1), name).toEqual([false])
      wrapper.unmount()
    }
  })

  it('useBillingStanding refreshes the shell standing from an app\'s own page', async () => {
    bridge.getStanding.mockResolvedValueOnce(standing(true)).mockResolvedValueOnce({ ...standing(true), standing: 'Good' })
    const { useBillingStanding } = await import('../../../src/billing')
    let api: ReturnType<typeof useBillingStanding> | null = null
    mount(defineComponent({ setup() { api = useBillingStanding(); return () => h('div') } }))

    await api!.refresh()
    expect(api!.standing.value?.standing).toBe('Warning')
    expect(api!.view.value).toBe('banner')
    await api!.refresh()
    expect(api!.standing.value?.standing).toBe('Good')
    expect(api!.view.value).toBe('normal')
  })

  it('the storage meter export shows used against included in GB', async () => {
    const { TBillingStorageMeter } = await import('../../../src/billing')
    const wrapper = hostOf(TBillingStorageMeter, { usage: meteredUsage(300, 10, 50) })
    await settle()
    expect(wrapper.find('[data-testid="billing-storage-used"]').text()).toBe('300 GB of 250 GB')
  })
})

describe('BillingHistorySection', () => {
  it('lists invoices and downloads the PDF through the bridge, not a link', async () => {
    bridge.listInvoices.mockResolvedValue({
      items: [
        { id: 'inv-1', invoiceNo: 'INV-001', status: 'Paid', amount: 99, currency: 'USD', invoiceDate: '2026-10-01T00:00:00Z' },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })
    const pdf = new Blob(['%PDF'])
    bridge.downloadInvoicePdf.mockResolvedValue(pdf)
    const { default: Section } = await import('../../../src/pages/billing/BillingHistorySection.vue')
    const wrapper = mount(Section, { global: { stubs } })
    await settle()

    expect(bridge.listInvoices).toHaveBeenCalledWith(1, 20)
    expect(wrapper.text()).toContain('INV-001')
    expect(wrapper.find('a[href]').exists()).toBe(false)

    await wrapper.find('[data-testid="billing-download-inv-1"]').trigger('click')
    await settle()

    expect(bridge.downloadInvoicePdf).toHaveBeenCalledWith('inv-1')
    expect(downloadBlob).toHaveBeenCalledWith(pdf, 'INV-001.pdf')
  })

  it('says receipts go to the payer while the account has no billing contact, and lets a manager set one', async () => {
    bridge.listInvoices.mockResolvedValue({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: true, displayName: 'Northwind', address: '100 King St', taxId: 'BN1', billingEmails: [] })
    bridge.updateAccount.mockImplementation(async (p: { billingEmails: string[] }) => ({
      accountType: 'Installation', accountId: 'main', canManage: true, displayName: 'Northwind', address: '100 King St', taxId: 'BN1', billingEmails: p.billingEmails,
    }))
    const { default: Section } = await import('../../../src/pages/billing/BillingHistorySection.vue')
    const wrapper = mount(Section, { global: { stubs } })
    await settle()

    expect(wrapper.find('[data-testid="billing-contacts-fallback"]').exists()).toBe(true)
    await wrapper.find('[data-testid="billing-contacts-edit"]').trigger('click')
    await wrapper.find('[data-testid="billing-contacts-input"] textarea').setValue('accounts@northwind.example\nowner@northwind.example')
    await wrapper.find('[data-testid="billing-contacts-save"]').trigger('click')
    await settle()

    // The PUT overwrites the whole profile: the other fields go back unchanged
    expect(bridge.updateAccount).toHaveBeenCalledWith({
      displayName: 'Northwind',
      address: '100 King St',
      taxId: 'BN1',
      billingEmails: ['accounts@northwind.example', 'owner@northwind.example'],
    })
    expect(wrapper.find('[data-testid="billing-contacts-list"]').text()).toBe('accounts@northwind.example, owner@northwind.example')
  })

  it('shows the billing contacts to a viewer without an edit button', async () => {
    bridge.listInvoices.mockResolvedValue({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })
    bridge.getAccount.mockResolvedValue({ accountType: 'Installation', accountId: 'main', canManage: false, billingEmails: ['accounts@northwind.example'] })
    const { default: Section } = await import('../../../src/pages/billing/BillingHistorySection.vue')
    const wrapper = mount(Section, { global: { stubs } })
    await settle()

    expect(wrapper.find('[data-testid="billing-contacts-list"]').text()).toBe('accounts@northwind.example')
    expect(wrapper.find('[data-testid="billing-contacts-edit"]').exists()).toBe(false)
  })

  it('shows what a receipt actually charged (tax included) and what a credit note gave back', async () => {
    bridge.listInvoices.mockResolvedValue({
      items: [
        { id: 'rcp-1', invoiceNo: 'RCP-1', type: 'Receipt', status: 'Paid', amount: 100, taxAmount: 13, dueAmount: 113, paidAmount: 113, currency: 'CAD', invoiceDate: '2026-10-01T00:00:00Z' },
        { id: 'crn-1', invoiceNo: 'CRN-1', type: 'CreditNote', status: 'Paid', amount: 100, taxAmount: 13, dueAmount: 113, paidAmount: 113, currency: 'CAD', invoiceDate: '2026-10-05T00:00:00Z' },
      ],
      totalCount: 2,
      pageIndex: 1,
      pageSize: 20,
    })
    const { default: Section } = await import('../../../src/pages/billing/BillingHistorySection.vue')
    const wrapper = mount(Section, { global: { stubs } })
    await settle()

    const rows = wrapper.findAll('tbody tr').map((r) => r.text())
    expect(rows.find((r) => r.includes('RCP-1'))).toContain('CA$113.00')
    expect(rows.find((r) => r.includes('RCP-1'))).not.toContain('CA$100.00')
    expect(rows.find((r) => r.includes('CRN-1'))).toContain('-CA$113.00')
  })

  it('calls out the storage overage a renewal receipt carried', async () => {
    bridge.listInvoices.mockResolvedValue({
      items: [
        {
          id: 'rcp-2', invoiceNo: 'RCP-2', type: 'Receipt', status: 'Paid', amount: 309, taxAmount: 40.17, dueAmount: 349.17, paidAmount: 349.17, currency: 'CAD', invoiceDate: '2026-11-01T00:00:00Z',
          lineItems: [
            { description: 'Firm', quantity: 1, unitPrice: 299, amount: 299, discountAmount: 0, taxRate: 0, taxAmount: 38.87 },
            { description: 'Storage above 250 GB included (GB-month)', quantity: 50, unitPrice: 0.2, amount: 10, discountAmount: 0, taxRate: 0, taxAmount: 1.3, kind: 'storage-overage' },
          ],
        },
        { id: 'rcp-1', invoiceNo: 'RCP-1', type: 'Receipt', status: 'Paid', amount: 299, taxAmount: 38.87, dueAmount: 337.87, paidAmount: 337.87, currency: 'CAD', invoiceDate: '2026-10-01T00:00:00Z', lineItems: [] },
      ],
      totalCount: 2,
      pageIndex: 1,
      pageSize: 20,
    })
    const { default: Section } = await import('../../../src/pages/billing/BillingHistorySection.vue')
    const wrapper = mount(Section, { global: { stubs } })
    await settle()

    expect(wrapper.find('[data-testid="billing-history-storage-rcp-2"]').text()).toBe('Includes storage overage CA$10.00')
    expect(wrapper.find('[data-testid="billing-history-storage-rcp-1"]').exists()).toBe(false)
  })

  it('shows a refused read in place of the list', async () => {
    bridge.listInvoices.mockRejectedValue(new Error('Forbidden'))
    const { default: Section } = await import('../../../src/pages/billing/BillingHistorySection.vue')
    const wrapper = mount(Section, { global: { stubs } })
    await settle()
    expect(wrapper.find('[data-testid="billing-history-error"]').text()).toContain('Forbidden')
  })
})
