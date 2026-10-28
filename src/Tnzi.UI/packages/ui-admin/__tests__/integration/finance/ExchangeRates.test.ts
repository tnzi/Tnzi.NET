import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Exchange Rates page - TCrudPage where create/update both route through the
 * idempotent upsert, plus a refresh-from-provider toolbar action.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/exchange-rates', fullPath: '/admin/finance/exchange-rates', hash: '', name: 'finance.rates', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const ratesFetch = vi.fn(async () => ({
  items: [{ id: 'r1', fromCurrency: 'EUR', toCurrency: 'USD', rate: 1.1, rateDate: '2026-01-01T00:00:00', source: 'Manual' }],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))
const refresh = vi.fn(async () => 5)

vi.mock('../../../src/services/bridges/finance-bridge', () => ({
  createFinanceBridge: () => ({
    rates: {
      fetch: ratesFetch,
      upsert: vi.fn(async (data: unknown) => ({ id: 'r2', ...(data as object) })),
      delete: vi.fn(async () => undefined),
      refresh,
    },
  }),
}))

import ExchangeRates from '../../../src/pages/finance/ExchangeRates.vue'

const stubs = {
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
}

interface RatesVm {
  refreshFromProvider: () => Promise<void>
}

describe('Finance ExchangeRates page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    ratesFetch.mockClear()
    refresh.mockClear()
  })

  it('mounts and fetches the rate list', async () => {
    mount(ExchangeRates, { global: { stubs } })
    await flushPromises()
    expect(ratesFetch).toHaveBeenCalledTimes(1)
  })

  // A rate is upserted by (from, to, date). Editing any of them saved a second
  // rate and left the mistaken one in force for postings on its date. On edit
  // they are shown, not edited; only the rate and its source change.
  it('★ editing a rate shows its currency pair and date without letting them change', async () => {
    const wrapper = mount(ExchangeRates, { global: { stubs } })
    await flushPromises()
    const crud = (wrapper.vm as unknown as { crud: { openEdit: (r: unknown) => void } }).crud

    crud.openEdit({ id: 'r1', fromCurrency: 'EUR', toCurrency: 'USD', rate: 1.1, rateDate: '2026-01-01T00:00:00', source: 'Manual' })
    await flushPromises()

    const locked = wrapper.findAll('input')
      .filter((i) => i.attributes('disabled') !== undefined)
      .map((i) => (i.element as HTMLInputElement).value)
    expect(locked).toEqual(['EUR', 'USD', 'Jan 1, 2026'])
  })

  it('a new rate takes its currency pair and date as input', async () => {
    const wrapper = mount(ExchangeRates, { global: { stubs } })
    await flushPromises()
    const crud = (wrapper.vm as unknown as { crud: { openCreate: () => void } }).crud

    crud.openCreate()
    await flushPromises()

    expect(wrapper.findAll('input').length).toBeGreaterThanOrEqual(4)
    expect(wrapper.findAll('input').some((i) => i.attributes('disabled') !== undefined)).toBe(false)
  })

  it('refresh-from-provider calls the bridge and refetches', async () => {
    const wrapper = mount(ExchangeRates, { global: { stubs } })
    await flushPromises()
    ratesFetch.mockClear()

    await (wrapper.vm as unknown as RatesVm).refreshFromProvider()
    await flushPromises()

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(ratesFetch).toHaveBeenCalled()
  })
})
