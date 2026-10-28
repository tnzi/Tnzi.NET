import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { roundMoney } from '../../../src/pages/finance/money'
import SettlementApplyPanel from '../../../src/pages/finance/components/SettlementApplyPanel.vue'

/**
 * Money the finance pages DERIVE in the browser (an outstanding balance, a
 * remaining amount, a sum of allocations) is IEEE-double arithmetic: in JS
 * `50.3 - 20.1` is `30.199999999999996`. Sent as an allocation, that leaves the
 * target a hair short of Paid forever; compared against a remaining amount, it
 * disables a submit that should be allowed.
 */
describe('roundMoney', () => {
  it('rounds a float tail to whole cents', () => {
    expect(roundMoney(50.3 - 20.1)).toBe(30.2)
    expect(roundMoney(0.1 + 0.2)).toBe(0.3)
  })

  it('rounds a half cent away from zero, as the ledger does', () => {
    // 1.005 is stored as 1.00499999999999989...; the decimal it stands for rounds up.
    expect(roundMoney(1.005)).toBe(1.01)
    expect(roundMoney(-1.005)).toBe(-1.01)
    expect(roundMoney(10.004)).toBe(10)
  })

  it('treats missing or non-finite input as zero and never returns negative zero', () => {
    expect(roundMoney(null)).toBe(0)
    expect(roundMoney(undefined)).toBe(0)
    expect(roundMoney(Number.NaN)).toBe(0)
    expect(Object.is(roundMoney(-0.001), 0)).toBe(true)
  })
})

describe('SettlementApplyPanel', () => {
  function mountPanel(remaining: number) {
    const apply = vi.fn(async () => [])
    const bridge = {
      settlements: {
        openDocuments: vi.fn(async () => [
          { docType: 'Invoice', docId: 'i1', number: 'INV-1', currency: 'USD', outstanding: 0.1 },
          { docType: 'Invoice', docId: 'i2', number: 'INV-2', currency: 'USD', outstanding: 0.2 },
        ]),
        apply,
      },
    }
    const wrapper = mount(SettlementApplyPanel, {
      props: {
        bridge: bridge as never,
        source: { id: 'p1', sourceType: 'PaymentEntry', partyType: 'Customer', partyId: 'c1', currency: 'USD', remaining } as never,
      },
    })
    return { wrapper, apply }
  }

  type PanelVm = {
    allocations: Record<string, number | null>
    allocatedTotal: number
    applyRemaining: number
    submitApply: () => Promise<void>
  }

  it('lets an allocation use up exactly the remaining amount', async () => {
    const { wrapper } = mountPanel(0.3)
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm

    vm.allocations.i1 = 0.1
    vm.allocations.i2 = 0.2
    await flushPromises()

    // 0.1 + 0.2 is 0.30000000000000004 in doubles: compared raw against a
    // remaining of 0.3, the submit button would stay disabled for a correct split.
    expect(vm.allocatedTotal).toBe(0.3)
    expect(vm.applyRemaining).toBe(0)
    const submit = wrapper.findAll('button').find((b) => b.classes().includes('n-button--primary-type'))
    expect(submit?.attributes('disabled')).toBeUndefined()
  })

  it('compares against the remaining amount in whole cents, whatever float the caller derived it as', async () => {
    // A caller computing remaining as `50.3 - 20.1` hands over 30.199999999999996.
    const { wrapper } = mountPanel(50.3 - 20.1)
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm

    vm.allocations.i1 = 30.2
    await flushPromises()

    expect(vm.applyRemaining).toBe(0)
    const submit = wrapper.findAll('button').find((b) => b.classes().includes('n-button--primary-type'))
    expect(submit?.attributes('disabled')).toBeUndefined()
  })

  it('sends allocations in whole cents and drops ones that round to nothing', async () => {
    const { wrapper, apply } = mountPanel(10)
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm

    vm.allocations.i1 = 0.1 + 0.2 - 0.1
    vm.allocations.i2 = 0.001
    await vm.submitApply()
    await flushPromises()

    expect(apply).toHaveBeenCalledTimes(1)
    const sent = (apply.mock.calls[0] as unknown as [{ targets: { targetId: string; amount: number }[] }])[0]
    expect(sent.targets).toEqual([{ targetType: 'Invoice', targetId: 'i1', amount: 0.2 }])
  })

  it('a failed open-documents read shows the reason, never "no open documents"', async () => {
    // "No open documents" tells the operator nothing is owed. After a refused
    // or failed read that is a guess, and the panel must not make it.
    const bridge = {
      settlements: {
        openDocuments: vi.fn(async () => {
          throw new Error('You do not have permission to view settlements.')
        }),
        apply: vi.fn(),
      },
    }
    const wrapper = mount(SettlementApplyPanel, {
      props: {
        bridge: bridge as never,
        source: { id: 'p1', sourceType: 'PaymentEntry', partyType: 'Customer', partyId: 'c1', currency: 'USD', remaining: 10 } as never,
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('You do not have permission to view settlements.')
    expect(wrapper.text()).not.toMatch(/no open/i)
  })
})
