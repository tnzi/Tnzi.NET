import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Transfers page - TCrudPage over the funds-transfer document workflow
 * (draft create/edit + conditional post/void/delete row actions).
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/transfers', fullPath: '/admin/finance/transfers', hash: '', name: 'finance.transfers', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchList = vi.fn(async () => ({
  items: [
    { id: 't1', number: 'TRF-000001', status: 'Posted', fromAccountId: 'a1', fromAccountName: '1120 Bank', toAccountId: 'a2', toAccountName: '1110 Cash', transferDate: '2026-03-10', currency: 'USD', amount: 300, baseAmount: 300 },
    { id: 't2', number: null, status: 'Draft', fromAccountId: 'a1', fromAccountName: '1120 Bank', toAccountId: 'a2', toAccountName: '1110 Cash', transferDate: '2026-03-12', currency: 'USD', amount: 50, baseAmount: 0 },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

const transferSection = {
  fetch: fetchList,
  getById: vi.fn(async () => null),
  createDraft: vi.fn(),
  updateDraft: vi.fn(),
  deleteDraft: vi.fn(),
  post: vi.fn(),
  voidDoc: vi.fn(),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    FinanceDocumentStatus: original.FinanceDocumentStatus,
    ReconciliationStatus: original.ReconciliationStatus,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      transfers: transferSection,
    }),
  }
})

import Page from '../../../src/pages/finance/Transfers.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
}

interface TransfersVm {
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }>
}

describe('Finance Transfers page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })

  it('gates post/void/delete row actions on document status', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as TransfersVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    const draft = { id: 't2', status: 'Draft' }
    const posted = { id: 't1', status: 'Posted' }

    expect(byKey.post!.show!(draft)).toBe(true)
    expect(byKey.post!.show!(posted)).toBe(false)
    expect(byKey.void!.show!(posted)).toBe(true)
    expect(byKey.void!.show!(draft)).toBe(false)
    expect(byKey.delete!.show!(draft)).toBe(true)
    expect(byKey.delete!.show!(posted)).toBe(false)
  })
})
