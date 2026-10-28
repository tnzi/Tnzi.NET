/**
 * Starting a subscription from the billing section: the empty state for a
 * billing manager versus a viewer, the plan list (price per cycle and tax),
 * the subscribe path through the real Stripe Payment Element (with
 * `@stripe/stripe-js` mocked), and what a failed or abandoned first payment
 * leaves behind (a Pending sign-up the manager finishes, never a second one).
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { resetStripeClientCache } from '@tnzi/core/services/payment'

const bridge = {
  getStanding: vi.fn(),
  getAccount: vi.fn(),
  updateAccount: vi.fn(),
  listSubscriptions: vi.fn(),
  payNow: vi.fn(),
  listPlans: vi.fn(),
  createSubscription: vi.fn(),
  cancelSubscription: vi.fn(),
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
  getStorageUsage: vi.fn(),
}

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))
vi.mock('../../../src/services/bridges/billing-bridge', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../src/services/bridges/billing-bridge')>()),
  createBillingBridge: () => bridge,
}))
vi.mock('../../../src/plugin/billing-config', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../src/plugin/billing-config')>()),
  useAdminBillingConfig: () => ({ productCode: 'firm', settingsSection: 'billing-subscription', state: { view: 'normal' } }),
}))

const element = { mount: vi.fn(), destroy: vi.fn(), on: vi.fn((e: string, h: () => void) => e === 'ready' && h()) }
const stripe = {
  elements: vi.fn(() => ({ create: vi.fn(() => element), submit: vi.fn() })),
  confirmSetup: vi.fn(),
  confirmPayment: vi.fn(),
}
vi.mock('@stripe/stripe-js', () => ({ loadStripe: vi.fn(async () => stripe) }))

const stubs = {
  TModalShell: {
    props: ['show', 'title'],
    template: '<div v-if="show" class="modal"><h3 class="modal-title">{{ title }}</h3><slot /><slot name="footer" /></div>',
  },
}

const firmMonthly = {
  id: 'plan-1',
  planCode: 'FIRM-M',
  productCode: 'firm',
  planName: 'Firm Monthly',
  price: 100,
  currency: 'CAD',
  cycleType: 'Month',
  cycleValue: 1,
  trialDays: 0,
  allowTrial: false,
  sortOrder: 1,
  isActive: true,
  tax: {
    taxIncluded: false,
    taxAmount: 13,
    payableAmount: 113,
    lines: [{ name: 'HST', rate: 13, amount: 13, registrationNumber: '123456789 RT0001' }],
  },
}

const firmYearly = {
  ...firmMonthly,
  id: 'plan-2',
  planCode: 'FIRM-Y',
  planName: 'Firm Yearly',
  price: 1000,
  cycleType: 'Year',
  sortOrder: 2,
  tax: { taxIncluded: true, taxAmount: 115.04, payableAmount: 1000, lines: [{ name: 'HST', rate: 13, amount: 115.04 }] },
}

const pendingSignUp = {
  id: 'sub-new',
  subscriptionNo: 'S-NEW',
  planName: 'Firm Monthly',
  status: 'Pending',
  originalPrice: 100,
  currency: 'CAD',
  nextBillingTime: null,
  hasPaymentMethod: false,
}

const active = { ...pendingSignUp, status: 'Active', nextBillingTime: '2026-11-26T00:00:00Z', hasPaymentMethod: true, paymentMethodBrand: 'visa', paymentMethodLast4: '4242' }
const ended = { ...pendingSignUp, id: 'sub-old', status: 'Cancelled' }

function account(canManage: boolean) {
  return { accountType: 'Installation', accountId: 'main', canManage, billingEmails: [] }
}

function standing(canManage: boolean) {
  return { standing: 'None', canView: true, canManage, gateEnabled: false, isExempt: false, subscriptionId: null }
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i++) {
    await vi.dynamicImportSettled()
    await flushPromises()
  }
}

async function mountSection(canManage: boolean) {
  bridge.getAccount.mockResolvedValue(account(canManage))
  bridge.getStanding.mockResolvedValue(standing(canManage))
  const { default: Section } = await import('../../../src/pages/billing/BillingSubscriptionSection.vue')
  const wrapper = mount(Section, { global: { stubs }, attachTo: document.body })
  await settle()
  return wrapper
}

beforeEach(() => {
  setActivePinia(createPinia())
  resetStripeClientCache()
  vi.clearAllMocks()
  bridge.listSubscriptions.mockResolvedValue([])
  bridge.listPaymentMethods.mockResolvedValue([])
  bridge.listPlans.mockResolvedValue([firmMonthly, firmYearly])
  bridge.getClientConfig.mockResolvedValue({ channelCode: 'Stripe', publishableKey: 'pk_test' })
  bridge.syncPayment.mockResolvedValue(undefined)
  bridge.cancelSubscription.mockResolvedValue(undefined)
  bridge.getStorageUsage.mockResolvedValue({ metered: false, usageAvailable: true, sampleDays: 0, overageQuantity: 0, accruedOverageAmount: 0, inTrial: false })
})

describe('the tax line', () => {
  it('names the cycle the same way the price line does', async () => {
    const quarterly = {
      ...firmMonthly,
      planCode: 'FIRM-Q',
      price: 250,
      cycleValue: 3,
      tax: { taxIncluded: false, taxAmount: 32.5, payableAmount: 282.5, lines: [{ name: 'HST', rate: 13, amount: 32.5 }] },
    }
    const yearly = { ...firmYearly, tax: { taxIncluded: false, taxAmount: 130, payableAmount: 1130, lines: [{ name: 'HST', rate: 13, amount: 130 }] } }
    bridge.listPlans.mockResolvedValue([quarterly, yearly])
    const wrapper = await mountSection(true)

    const q = wrapper.find('[data-testid="billing-plan-FIRM-Q"]')
    expect(q.find('[data-testid="billing-plan-price"]').text()).toBe('CA$250.00 every 3 months')
    expect(q.find('[data-testid="billing-plan-tax"]').text()).toBe('Plus HST 13%: CA$282.50 every 3 months')
    expect(wrapper.find('[data-testid="billing-plan-FIRM-Y"] [data-testid="billing-plan-tax"]').text()).toBe('Plus HST 13%: CA$1,130.00 per year')
  })
})

const GB = 1073741824

function offerUsage(usedGb: number, includedGb: number | null) {
  return {
    metered: true,
    subscribed: false,
    usageAvailable: true,
    includedBytes: includedGb === null ? null : includedGb * GB,
    usedBytes: usedGb * GB,
    sampledOn: '2026-10-27T14:00:00Z',
    sampleDays: 0,
    overageQuantity: 0,
    overageUnitPrice: includedGb === null ? null : 0.2,
    accruedOverageAmount: 0,
    currency: 'CAD',
    inTrial: false,
  }
}

describe('storage before subscribing', () => {
  it('shows what the account stores against the offered plan\'s allowance', async () => {
    bridge.listPlans.mockResolvedValue([{ ...firmMonthly, includedStorageBytes: 250 * GB, storageOveragePricePerGbMonth: 0.2 }])
    bridge.getStorageUsage.mockResolvedValue(offerUsage(180, 250))
    const wrapper = await mountSection(true)

    const offer = wrapper.find('[data-testid="billing-storage-offer"]')
    expect(offer.find('[data-testid="billing-storage-used"]').text()).toBe('180 GB of 250 GB')
    expect(offer.find('[data-testid="billing-storage-over"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-storage-overage"]').exists()).toBe(false)
  })

  it('warns when the account is already above the allowance, naming the rate but no amount', async () => {
    bridge.listPlans.mockResolvedValue([{ ...firmMonthly, includedStorageBytes: 250 * GB, storageOveragePricePerGbMonth: 0.2 }])
    bridge.getStorageUsage.mockResolvedValue(offerUsage(310, 250))
    const wrapper = await mountSection(true)

    const offer = wrapper.find('[data-testid="billing-storage-offer"]')
    expect(offer.find('[data-testid="billing-storage-used"]').text()).toBe('310 GB of 250 GB')
    expect(offer.find('.t-billing-storage--over').exists()).toBe(true)
    expect(offer.find('[data-testid="billing-storage-over"]').text()).toContain('CA$0.20 per GB-month')
    expect(wrapper.find('[data-testid="billing-storage-overage"]').exists()).toBe(false)
  })

  it('shows usage alone when the product sells several plans', async () => {
    bridge.getStorageUsage.mockResolvedValue(offerUsage(310, null))
    const wrapper = await mountSection(true)

    expect(wrapper.find('[data-testid="billing-storage-used"]').text()).toBe('310 GB in use')
    expect(wrapper.find('[data-testid="billing-storage-over"]').exists()).toBe(false)
  })

  it('shows the reading for an unpaid sign-up too', async () => {
    bridge.listSubscriptions.mockResolvedValue([pendingSignUp])
    bridge.getStorageUsage.mockResolvedValue({ ...offerUsage(310, 250), subscriptionId: 'sub-new' })
    const wrapper = await mountSection(true)

    expect(wrapper.find('[data-testid="billing-pending-notice"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-storage-offer"] [data-testid="billing-storage-used"]').text()).toBe('310 GB of 250 GB')
  })

  it('shows a viewer the reading as well', async () => {
    bridge.getStorageUsage.mockResolvedValue(offerUsage(180, 250))
    const wrapper = await mountSection(false)

    expect(wrapper.find('[data-testid="billing-no-subscription"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-storage-used"]').text()).toBe('180 GB of 250 GB')
  })
})

describe('a product that sells a single plan', () => {
  const firmOnly = { ...firmMonthly, includedStorageBytes: 250 * 1073741824, storageOveragePricePerGbMonth: 0.2 }

  it('shows that plan directly, with its storage, instead of a list to choose from', async () => {
    bridge.listPlans.mockResolvedValue([firmOnly])
    const wrapper = await mountSection(true)

    const offer = wrapper.find('[data-testid="billing-plan-FIRM-M"]')
    expect(offer.attributes('data-single-plan')).toBe('true')
    expect(wrapper.text()).not.toContain('Choose a plan to start the subscription')
    expect(offer.find('[data-testid="billing-plan-storage"]').text()).toBe('Includes 250 GB of storage, then CA$0.20 per GB-month')
    expect(offer.find('[data-testid="billing-plan-subscribe-FIRM-M"]').exists()).toBe(true)
  })

  it('keeps the catalogue when there are several plans', async () => {
    const wrapper = await mountSection(true)

    expect(wrapper.find('[data-single-plan]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Choose a plan to start the subscription')
  })

  it('does not offer to choose another plan for an unpaid sign-up when there is no other plan', async () => {
    bridge.listSubscriptions.mockResolvedValue([pendingSignUp])
    bridge.listPlans.mockResolvedValue([firmOnly])
    const single = await mountSection(true)
    expect(single.find('[data-testid="billing-finish-payment"]').exists()).toBe(true)
    expect(single.find('[data-testid="billing-choose-another-plan"]').exists()).toBe(false)

    bridge.listPlans.mockResolvedValue([firmMonthly, firmYearly])
    const several = await mountSection(true)
    expect(several.find('[data-testid="billing-choose-another-plan"]').exists()).toBe(true)
  })
})

describe('BillingSubscriptionSection without a subscription', () => {
  it('offers a billing manager the active plans of the app product', async () => {
    const wrapper = await mountSection(true)

    expect(bridge.listPlans).toHaveBeenCalledWith('firm')
    const monthly = wrapper.find('[data-testid="billing-plan-FIRM-M"]')
    expect(monthly.text()).toContain('Firm Monthly')
    expect(monthly.find('[data-testid="billing-plan-price"]').text()).toBe('CA$100.00 per month')
    expect(monthly.find('[data-testid="billing-plan-tax"]').text()).toBe('Plus HST 13%: CA$113.00 per month')
    const yearly = wrapper.find('[data-testid="billing-plan-FIRM-Y"]')
    expect(yearly.find('[data-testid="billing-plan-price"]').text()).toBe('CA$1,000.00 per year')
    expect(yearly.find('[data-testid="billing-plan-tax"]').text()).toBe('Price includes HST 13%')
    expect(wrapper.find('[data-testid="billing-no-subscription"]').exists()).toBe(false)
  })

  it('shows a viewer a read-only line and asks for no plans', async () => {
    const wrapper = await mountSection(false)

    expect(wrapper.find('[data-testid="billing-no-subscription"]').text()).toContain('No subscription yet')
    expect(wrapper.find('[data-testid="billing-plan-list"]').exists()).toBe(false)
    expect(bridge.listPlans).not.toHaveBeenCalled()
  })

  it('treats an ended subscription as none, so the account can subscribe again', async () => {
    bridge.listSubscriptions.mockResolvedValue([ended])
    const wrapper = await mountSection(true)

    expect(wrapper.find('[data-testid="billing-plan-list"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-subscription-facts"]').exists()).toBe(false)
  })

  it('says so when a plan has no tax added, and prints nothing when the server could not say', async () => {
    bridge.listPlans.mockResolvedValue([
      { ...firmMonthly, tax: { taxIncluded: false, taxAmount: 0, payableAmount: 100, lines: [] } },
      { ...firmYearly, tax: null },
    ])
    const wrapper = await mountSection(true)

    expect(wrapper.find('[data-testid="billing-plan-FIRM-M"] [data-testid="billing-plan-tax"]').text()).toBe('No tax added')
    expect(wrapper.find('[data-testid="billing-plan-FIRM-Y"] [data-testid="billing-plan-tax"]').exists()).toBe(false)
  })
})

describe('subscribing', () => {
  it('creates the subscription, charges the first period through the Payment Element and lands on the subscription', async () => {
    bridge.createSubscription.mockResolvedValue({
      subscription: pendingSignUp,
      payment: { tradeNo: 'T-1', payParams: 'pi_secret', amount: 113, originalAmount: 100, discountAmount: 0, taxAmount: 13, currency: 'CAD' },
      requiresPayment: true,
    })
    stripe.confirmPayment.mockResolvedValue({ paymentIntent: { id: 'pi_1', status: 'succeeded' } })
    const wrapper = await mountSection(true)

    await wrapper.find('[data-testid="billing-plan-subscribe-FIRM-M"]').trigger('click')
    await settle()
    expect(bridge.createSubscription).not.toHaveBeenCalled()

    await wrapper.find('[data-testid="subscribe-continue"]').trigger('click')
    await settle()
    expect(bridge.createSubscription).toHaveBeenCalledWith('plan-1')
    expect(stripe.elements).toHaveBeenCalledWith({ clientSecret: 'pi_secret' })
    expect(wrapper.find('[data-testid="subscribe-amount"]').text()).toBe('CA$113.00')

    bridge.listSubscriptions.mockResolvedValue([active])
    await wrapper.find('[data-testid="subscribe-pay"]').trigger('click')
    await settle()

    expect(stripe.confirmPayment).toHaveBeenCalledWith(expect.objectContaining({ redirect: 'if_required' }))
    expect(bridge.syncPayment).toHaveBeenCalledWith('T-1')
    expect(wrapper.find('[data-testid="subscribe-pay"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-subscription-facts"]').text()).toContain('Firm Monthly')
    expect(wrapper.find('[data-testid="billing-renewal-card"]').text()).toContain('visa **** 4242')
    expect(wrapper.find('[data-testid="billing-plan-list"]').exists()).toBe(false)
  })

  it('keeps a declined first payment open for another card, then offers to finish it once the dialog is closed', async () => {
    bridge.createSubscription.mockResolvedValue({
      subscription: pendingSignUp,
      payment: { tradeNo: 'T-1', payParams: 'pi_secret', amount: 113, originalAmount: 100, discountAmount: 0, taxAmount: 13, currency: 'CAD' },
      requiresPayment: true,
    })
    stripe.confirmPayment.mockResolvedValue({ error: { type: 'card_error', message: 'Your card was declined.' } })
    const wrapper = await mountSection(true)

    await wrapper.find('[data-testid="billing-plan-subscribe-FIRM-M"]').trigger('click')
    await settle()
    await wrapper.find('[data-testid="subscribe-continue"]').trigger('click')
    await settle()
    await wrapper.find('[data-testid="subscribe-pay"]').trigger('click')
    await settle()

    expect(wrapper.text()).toContain('Your card was declined.')
    expect(bridge.syncPayment).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="subscribe-pay"]').exists()).toBe(true)
    expect(bridge.cancelSubscription).not.toHaveBeenCalled()

    // Abandon: the sign-up is still Pending on the server with its first payment open.
    bridge.listSubscriptions.mockResolvedValue([pendingSignUp])
    const cancel = wrapper.findAll('.modal button').find((b) => b.text() === 'Cancel')!
    await cancel.trigger('click')
    await settle()

    expect(wrapper.find('[data-testid="billing-pending-notice"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-plan-list"]').exists()).toBe(false)

    bridge.payNow.mockResolvedValue({
      subscriptionId: 'sub-new',
      tradeNo: 'T-1',
      payParams: 'pi_secret',
      amount: 113,
      taxAmount: 13,
      currency: 'CAD',
      channelCode: 'Stripe',
      settledWithoutPayment: false,
    })
    await wrapper.find('[data-testid="billing-finish-payment"]').trigger('click')
    await settle()

    expect(bridge.payNow).toHaveBeenCalledWith('sub-new')
    expect(bridge.createSubscription).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.modal-title').text()).toBe('Finish First Payment')
  })

  it('tells the manager to start again when the first payment can no longer be paid', async () => {
    bridge.listSubscriptions.mockResolvedValue([pendingSignUp])
    bridge.payNow.mockRejectedValue(Object.assign(new Error('SUBSCRIPTION_CANNOT_PAY_NOW'), { errorCode: 'SUBSCRIPTION_CANNOT_PAY_NOW' }))
    const wrapper = await mountSection(true)

    await wrapper.find('[data-testid="billing-finish-payment"]').trigger('click')
    await settle()

    expect(wrapper.find('[data-testid="pay-now-error"]').text()).toBe('This sign-up can no longer be paid. Choose a plan to start again.')
  })

  it('drops the unpaid sign-up and shows the plans when the manager chooses another plan', async () => {
    bridge.listSubscriptions.mockResolvedValue([pendingSignUp])
    const wrapper = await mountSection(true)

    bridge.listSubscriptions.mockResolvedValue([{ ...pendingSignUp, status: 'Cancelled' }])
    await wrapper.find('[data-testid="billing-choose-another-plan"]').trigger('click')
    await settle()

    expect(bridge.cancelSubscription).toHaveBeenCalledWith('sub-new', expect.any(String))
    expect(wrapper.find('[data-testid="billing-plan-list"]').exists()).toBe(true)
  })

  it('cancels a sign-up it cannot take card payment for, so it does not block the next try', async () => {
    bridge.createSubscription.mockResolvedValue({
      subscription: pendingSignUp,
      payment: { tradeNo: 'T-1', payParams: null, amount: 113, originalAmount: 100, discountAmount: 0, taxAmount: 13, currency: 'CAD' },
      requiresPayment: true,
    })
    const wrapper = await mountSection(true)

    await wrapper.find('[data-testid="billing-plan-subscribe-FIRM-M"]').trigger('click')
    await settle()
    await wrapper.find('[data-testid="subscribe-continue"]').trigger('click')
    await settle()

    expect(wrapper.find('[data-testid="subscribe-error"]').exists()).toBe(true)
    expect(bridge.cancelSubscription).toHaveBeenCalledWith('sub-new', expect.any(String))
    expect(stripe.elements).not.toHaveBeenCalled()
  })

  it('shows a viewer a Pending sign-up without the payment actions', async () => {
    bridge.listSubscriptions.mockResolvedValue([pendingSignUp])
    const wrapper = await mountSection(false)

    expect(wrapper.find('[data-testid="billing-pending-notice"]').text()).toContain('first payment has not been completed')
    expect(wrapper.find('[data-testid="billing-finish-payment"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-choose-another-plan"]').exists()).toBe(false)
  })
})
