import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PartyBankAccountsPanel from '../../../src/pages/finance/components/PartyBankAccountsPanel.vue'

/**
 * The remit-to list decides where a vendor payment is sent. "No bank accounts
 * on file" after a read that failed is a guess, and it invites adding a second
 * copy of an account that is already there.
 */
describe('PartyBankAccountsPanel', () => {
  function mountPanel(byParty: () => Promise<unknown>) {
    setActivePinia(createPinia())
    return mount(PartyBankAccountsPanel, {
      props: {
        bridge: { partyBankAccounts: { byParty: vi.fn(byParty) } } as never,
        partyType: 'Vendor' as never,
        partyId: 'v1',
        t: (key: string) => key,
        canManage: true,
      },
      global: { stubs: { TResponsiveTable: { template: '<div class="table-stub" />' } } },
    })
  }

  it('a failed read says why instead of "no accounts on file"', async () => {
    const wrapper = mountPanel(async () => {
      throw new Error('Bank details are unavailable.')
    })
    await flushPromises()

    expect(wrapper.text()).toContain('Bank details are unavailable.')
    expect(wrapper.text()).not.toContain('remitTo.empty')
  })

  it('an answered empty list still says there are none', async () => {
    const wrapper = mountPanel(async () => [])
    await flushPromises()

    expect(wrapper.text()).toContain('remitTo.empty')
  })
})
