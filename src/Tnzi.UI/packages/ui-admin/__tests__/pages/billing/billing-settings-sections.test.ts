import { describe, it, expect } from 'vitest'
import { billingSettingsSections, BILLING_SUBSCRIPTION_SECTION_KEY } from '../../../src/billing'

describe('billingSettingsSections', () => {
  it('registers the subscription (with its cards) and billing history in order, gated on payment.account.view', () => {
    const sections = billingSettingsSections()

    // The cards live in the subscription section, which keeps its key: ManageUrl and the dunning mail deep-link to it
    expect(sections.map((s) => s.key)).toEqual([BILLING_SUBSCRIPTION_SECTION_KEY, 'billing-history'])
    expect(BILLING_SUBSCRIPTION_SECTION_KEY).toBe('billing-subscription')
    expect(sections.map((s) => s.order)).toEqual([10, 11])
    expect(sections.every((s) => s.permission === 'payment.account.view')).toBe(true)
  })

  it('loads each section on first open, not with the shell', () => {
    for (const section of billingSettingsSections()) {
      expect(typeof section.component).toBe('function')
    }
  })

  it('takes a group, a starting order and a permission', () => {
    const sections = billingSettingsSections({ group: 'Account', order: 50, permission: 'firm.billing.view' })

    expect(sections.every((s) => s.group === 'Account' && s.permission === 'firm.billing.view')).toBe(true)
    expect(sections.map((s) => s.order)).toEqual([50, 51])
  })
})
