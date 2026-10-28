import { describe, it, expect, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import TStripePaymentForm from '../../../src/pages/billing/TStripePaymentForm.vue'

// The application did not install the optional peer.
vi.mock('@stripe/stripe-js', () => {
  throw new Error("Cannot find module '@stripe/stripe-js'")
})

describe('TStripePaymentForm without @stripe/stripe-js', () => {
  it('shows what to install instead of crashing', async () => {
    const wrapper = mount(TStripePaymentForm, {
      props: { publishableKey: 'pk_test_missing', clientSecret: 'seti_secret', mode: 'setup' },
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="stripe-error"]').text()).toBe('Card collection needs the @stripe/stripe-js package')
    expect(wrapper.emitted('error')?.[0]).toEqual(['Card collection needs the @stripe/stripe-js package'])
    await expect((wrapper.vm as unknown as { confirm: () => Promise<unknown> }).confirm()).rejects.toThrow('@stripe/stripe-js')
  })
})
