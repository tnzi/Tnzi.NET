/**
 * The party picker fills its options from a request, and the request can be
 * superseded while it runs: the money direction flips it from customers to
 * vendors, or the operator types a new search. Whatever answers last must not
 * decide the options: a slower customer list landing on a vendor field is a
 * wrong-party submit.
 */
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import TPartySelect from '../../../src/components/finance/TPartySelect.vue'

const SelectStub = { name: 'Select', props: ['options', 'value', 'loading'], template: '<i />' }

function deferred<T>() {
  let resolve!: (v: T) => void
  const promise = new Promise<T>((r) => { resolve = r })
  return { promise, resolve }
}

const partyPage = (items: Array<{ id: string; name: string }>) => ({ items, totalCount: items.length, pageIndex: 1, pageSize: 50 })

describe('TPartySelect - only the latest request fills the list', () => {
  it('keeps the vendors when the customer list for the previous direction answers last', async () => {
    const slowCustomers = deferred<ReturnType<typeof partyPage>>()
    const bridge = {
      customers: { fetch: vi.fn(() => slowCustomers.promise) },
      vendors: { fetch: vi.fn(async () => partyPage([{ id: 'v1', name: 'Paper Co' }])) },
    }
    const w = mount(TPartySelect, { props: { bridge: bridge as never, amount: 100 }, global: { stubs: { Select: SelectStub } } })

    await w.setProps({ amount: -100 })
    await flushPromises()
    slowCustomers.resolve(partyPage([{ id: 'c1', name: 'Acme' }]))
    await flushPromises()

    const select = w.findComponent(SelectStub)
    expect(select.props('options')).toEqual([{ label: 'Paper Co', value: 'v1' }])
    expect(select.props('loading')).toBe(false)
  })
})
