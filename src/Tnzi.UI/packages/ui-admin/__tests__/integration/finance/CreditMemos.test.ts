import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Credit memos page - read-only list + #detail drawer + useDetail line editor.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/credit-memos', fullPath: '/admin/finance/credit-memos', hash: '', name: 'finance.creditMemos', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchList = vi.fn(async () => ({ items: [{ id: 'd1', number: 'CM-000001', status: 'Posted', docDate: '2026-07-01', currency: 'USD', total: 30, appliedTotal: 0 }], totalCount: 1, pageIndex: 1, pageSize: 20 }))

const docSection = {
  fetch: fetchList,
  getById: vi.fn(async () => null),
  createDraft: vi.fn(),
  updateDraft: vi.fn(),
  deleteDraft: vi.fn(),
  post: vi.fn(),
  voidDoc: vi.fn(),
}
const crudSection = {
  fetch: fetchList,
  create: vi.fn(),
  update: vi.fn(),
  delete: vi.fn(),
}
const listOnly = vi.fn(async () => [])

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    FinanceDocumentStatus: original.FinanceDocumentStatus,
    PaymentDirection: original.PaymentDirection,
    FinancePartyType: original.FinancePartyType,
    SettlementDocType: original.SettlementDocType,
    PAYMENT_METHODS: original.PAYMENT_METHODS,
    ItemType: original.ItemType,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      customers: crudSection,
      vendors: crudSection,
      items: crudSection,
      taxes: {
        agencies: listOnly, rates: listOnly, codes: listOnly,
        createAgency: vi.fn(), updateAgency: vi.fn(), deleteAgency: vi.fn(),
        createRate: vi.fn(), updateRate: vi.fn(), deleteRate: vi.fn(),
        createCode: vi.fn(), updateCode: vi.fn(), deleteCode: vi.fn(),
      },
      invoices: docSection,
      bills: docSection,
      expenses: docSection,
      creditMemos: docSection,
      payments: docSection,
      settlements: { applications: listOnly, openDocuments: listOnly, apply: vi.fn(), unapply: vi.fn() },
    }),
  }
})

import Page from '../../../src/pages/finance/CreditMemos.vue'
import { applyDocumentSearch } from './document-search.helper'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Descriptions: { name: 'Descriptions', template: '<div><slot /></div>' },
  DescriptionsItem: { name: 'DescriptionsItem', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  TabPane: { name: 'TabPane', template: '<div><slot /></div>' },
}

describe('Finance CreditMemos page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    listOnly.mockClear()
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })
  // The list endpoint binds its filters by name: credit memos filter by customer (`customerId`), and a
  // key it does not declare is dropped without an error, so the list would
  // claim to be filtered while showing every party. The date goes out as the
  // picked calendar day; the picker's epoch number fails model binding.
  it('the advanced search filters by the party key the list endpoint binds, and a calendar date', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const filters = await applyDocumentSearch(wrapper)
    expect(filters).toEqual({ dateFrom: '2026-01-15', customerId: 'party-1' })
  })

  it('keeps Apply on a partly applied memo until its credit is used up', async () => {
    // The first application moves a memo to PartiallyPaid, and the settlement
    // service accepts a PartiallyPaid memo as a source again. This page is the
    // only place a credit memo is applied from, so hiding Apply there strands
    // the rest of the credit.
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }> }
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))

    const posted = { id: 'm1', status: 'Posted', total: 500, appliedTotal: 0 }
    const partlyApplied = { id: 'm2', status: 'PartiallyPaid', total: 500, appliedTotal: 200 }
    const usedUp = { id: 'm3', status: 'Paid', total: 500, appliedTotal: 500 }

    expect(byKey.apply!.show!(posted)).toBe(true)
    expect(byKey.apply!.show!(partlyApplied)).toBe(true)
    expect(byKey.apply!.show!(usedUp)).toBe(false)
    // Void stays limited to a memo nothing has been applied from.
    expect(byKey.void!.show!(posted)).toBe(true)
    expect(byKey.void!.show!(partlyApplied)).toBe(false)
  })
})
