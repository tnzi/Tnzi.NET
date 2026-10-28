import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { resetStripeClientCache } from '@tnzi/core/services/payment'
import type { App } from 'vue'
import TStripePaymentForm from '../../../src/pages/billing/TStripePaymentForm.vue'
import { provideAdminBillingConfig } from '../../../src/plugin/billing-config'

const element = {
  mount: vi.fn(),
  destroy: vi.fn(),
  on: vi.fn((event: string, handler: () => void) => {
    if (event === 'ready') handler()
  }),
}
const elements = { create: vi.fn(() => element), submit: vi.fn() }
const stripe = {
  elements: vi.fn(() => elements),
  confirmSetup: vi.fn(),
  confirmPayment: vi.fn(),
}
const loadStripe = vi.fn(async () => stripe)

vi.mock('@stripe/stripe-js', () => ({ loadStripe: (key: string) => loadStripe(key) }))

type Exposed = { confirm: () => Promise<{ paymentMethodId?: string; status?: string }> }

async function mountForm(mode: 'setup' | 'payment', props: Record<string, unknown> = {}, stripeLink?: boolean) {
  // `stripeLink` given = the app enabled billing with that setting; absent = no billing config at all.
  const plugins =
    stripeLink === undefined ? [] : [{ install: (app: App) => provideAdminBillingConfig(app, { enabled: true, stripeLink }) }]
  const wrapper = mount(TStripePaymentForm, {
    props: { publishableKey: 'pk_test_1', clientSecret: 'seti_secret', mode, ...props },
    global: { plugins },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}

describe('TStripePaymentForm', () => {
  beforeEach(() => {
    resetStripeClientCache()
    vi.clearAllMocks()
  })

  it('loads Stripe.js on mount and mounts the Payment Element for the client secret', async () => {
    const wrapper = await mountForm('setup')
    expect(loadStripe).toHaveBeenCalledWith('pk_test_1')
    expect(stripe.elements).toHaveBeenCalledWith({ clientSecret: 'seti_secret' })
    expect(elements.create).toHaveBeenCalledWith('payment', { wallets: { link: 'never' } })
    expect(element.mount).toHaveBeenCalledWith(wrapper.find('[data-testid="stripe-element"]').element)
    expect(wrapper.emitted('ready')).toHaveLength(1)
    wrapper.unmount()
    expect(element.destroy).toHaveBeenCalled()
  })

  it('keeps Stripe Link off when the billing config does not ask for it', async () => {
    await mountForm('setup', {}, false)
    expect(elements.create).toHaveBeenCalledWith('payment', { wallets: { link: 'never' } })
  })

  it('offers Stripe Link when the billing config turns it on', async () => {
    await mountForm('setup', {}, true)
    expect(elements.create).toHaveBeenCalledWith('payment', { wallets: { link: 'auto' } })
  })

  it('lets the link prop override the billing config either way', async () => {
    await mountForm('setup', { link: true })
    expect(elements.create).toHaveBeenLastCalledWith('payment', { wallets: { link: 'auto' } })
    await mountForm('setup', { link: false }, true)
    expect(elements.create).toHaveBeenLastCalledWith('payment', { wallets: { link: 'never' } })
  })

  it('confirms a setup in place and returns the PaymentMethod id', async () => {
    stripe.confirmSetup.mockResolvedValue({ setupIntent: { id: 'seti_1', status: 'succeeded', payment_method: 'pm_123' } })
    const wrapper = await mountForm('setup')

    const result = await (wrapper.vm as unknown as Exposed).confirm()

    expect(stripe.confirmSetup).toHaveBeenCalledWith(expect.objectContaining({ elements, redirect: 'if_required' }))
    expect(result.paymentMethodId).toBe('pm_123')
  })

  it('confirms a payment and returns the PaymentIntent status', async () => {
    stripe.confirmPayment.mockResolvedValue({ paymentIntent: { id: 'pi_1', status: 'succeeded' } })
    const wrapper = await mountForm('payment', { clientSecret: 'pi_secret' })

    const result = await (wrapper.vm as unknown as Exposed).confirm()

    expect(stripe.confirmPayment).toHaveBeenCalledWith(expect.objectContaining({ redirect: 'if_required' }))
    expect(result.status).toBe('succeeded')
  })

  it("shows a card error in the payer's words and rejects", async () => {
    stripe.confirmPayment.mockResolvedValue({ error: { type: 'card_error', message: 'Your card was declined.' } })
    const wrapper = await mountForm('payment')

    await expect((wrapper.vm as unknown as Exposed).confirm()).rejects.toThrow('Your card was declined.')
    await flushPromises()
    expect(wrapper.find('[data-testid="stripe-error"]').text()).toContain('Your card was declined.')
  })

  it('does not show the raw text of an integration error', async () => {
    stripe.confirmSetup.mockResolvedValue({ error: { type: 'invalid_request_error', message: 'No such setupintent: seti_x' } })
    const wrapper = await mountForm('setup')

    await expect((wrapper.vm as unknown as Exposed).confirm()).rejects.toThrow()
    await flushPromises()
    const text = wrapper.find('[data-testid="stripe-error"]').text()
    expect(text).not.toContain('seti_x')
    expect(text).toContain('could not be confirmed')
  })

  it('says card payments are not configured when there is no publishable key', async () => {
    const wrapper = await mountForm('setup', { publishableKey: '' })
    expect(loadStripe).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="stripe-error"]').text()).toContain('not configured')
  })

  it('reports a Stripe.js that could not load', async () => {
    loadStripe.mockResolvedValueOnce(null as never)
    const wrapper = await mountForm('setup', { publishableKey: 'pk_test_blocked' })
    expect(wrapper.find('[data-testid="stripe-error"]').text()).toContain('Could not load the card form')
  })
})
