import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Revaluations page - read-only history of revaluation summary vouchers
 * (journal entries with sourceType=Revaluation) + a Run modal that previews
 * per-account increments before posting.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/revaluations', fullPath: '/admin/finance/revaluations', hash: '', name: 'finance.revaluations', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchHistory = vi.fn(async () => ({
  items: [
    { id: 'v1', number: 'JE-000090', status: 'Posted', postingDate: '2026-03-31', memo: 'FX revaluation', currency: 'USD', totalDebit: 120, totalCredit: 120, sourceType: 'Revaluation', sourceId: '2026-03-31' },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const previewFn = vi.fn(async () => ({
  asOf: '2026-03-31',
  baseCurrency: 'USD',
  journalEntryId: null,
  rows: [
    { accountId: 'a1', code: '1131', name: 'USD Wallet', currency: 'EUR', txnBalance: 1000, rate: 1.1, targetBase: 1100, bookBase: 1080, adjustment: 20, skipReason: null },
    { accountId: 'a2', code: '1132', name: 'Old Wallet', currency: 'GBP', txnBalance: 0, rate: 1.3, targetBase: 0, bookBase: 0, adjustment: 0, skipReason: 'Account inactive' },
  ],
  totalAdjustment: 20,
}))

const runFn = vi.fn(async () => ({ asOf: '2026-03-31', baseCurrency: 'USD', journalEntryId: 'v2', rows: [], totalAdjustment: 20 }))

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    JournalEntryStatus: original.JournalEntryStatus,
    createFinanceBridge: () => ({
      journals: { fetch: fetchHistory },
      revaluations: { preview: previewFn, run: runFn },
    }),
  }
})

import Page from '../../../src/pages/finance/Revaluations.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Input: { name: 'Input', template: '<input />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
}

interface RevalVm {
  doPreview: () => Promise<void>
  execute: () => Promise<void>
  hasPostable: boolean
  selectedAccountIds: string[]
  preview: { totalAdjustment: number } | null
}

describe('Finance Revaluations page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchHistory.mockClear()
    previewFn.mockClear()
    runFn.mockClear()
  })

  it('loads revaluation history filtered by sourceType', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchHistory.mock.calls.length).toBeGreaterThan(0)
    const query = fetchHistory.mock.calls[0]![0] as { filters?: Record<string, unknown> }
    expect(query.filters?.sourceType).toBe('Revaluation')
  })

  it('previews then posts, pre-selecting postable accounts only', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as RevalVm
    await vm.doPreview()
    expect(previewFn).toHaveBeenCalledOnce()
    // Only the non-skipped, non-zero-adjustment account gets pre-selected.
    expect(vm.selectedAccountIds).toEqual(['a1'])
    expect(vm.hasPostable).toBe(true)

    await vm.execute()
    expect(runFn).toHaveBeenCalledOnce()
    const arg = runFn.mock.calls[0]![0] as { asOf: string; accountIds: string[] | null }
    expect(arg.accountIds).toEqual(['a1'])
  })
})
