import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { Component } from 'vue'

/**
 * Every finance list opens a row in a read-only drawer that loads the full
 * record. On a slow link an operator opens A, then B, and A's answer arrives
 * last: the drawer for B must not paint A's lines and totals. The pages that
 * loaded the record themselves kept whichever answer came last.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance', fullPath: '/admin/finance', hash: '', name: 'finance', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const pending = vi.hoisted(() => new Map<string, (value: unknown) => void>())
const getById = vi.fn((id: string) => new Promise((resolve) => { pending.set(id, resolve) }))
const applications = vi.fn(async (_type: unknown, id: string) => [{ id: `app-${id}`, targetId: `t-${id}`, appliedAmount: 1 }])
const emptyPage = async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })
const listOnly = vi.fn(async () => [])

const docSection = {
  fetch: vi.fn(emptyPage),
  getById,
  createDraft: vi.fn(),
  updateDraft: vi.fn(),
  deleteDraft: vi.fn(),
  post: vi.fn(),
  voidDoc: vi.fn(),
  reverse: vi.fn(),
  send: vi.fn(),
  accept: vi.fn(),
  decline: vi.fn(),
  close: vi.fn(),
  convert: vi.fn(),
  update: vi.fn(),
  delete: vi.fn(),
}
const crudSection = { fetch: vi.fn(emptyPage), create: vi.fn(), update: vi.fn(), delete: vi.fn() }

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    ...original,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []), fetch: vi.fn(emptyPage) },
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
      journals: docSection,
      estimates: docSection,
      purchaseOrders: docSection,
      reports: { arAging: vi.fn(async () => ({ totals: { total: 0, current: 0 }, rows: [] })), apAging: vi.fn(async () => ({ totals: { total: 0, current: 0 }, rows: [] })) },
      settlements: { applications, openDocuments: listOnly, apply: vi.fn(), unapply: vi.fn() },
    }),
  }
})

import Invoices from '../../../src/pages/finance/Invoices.vue'
import Bills from '../../../src/pages/finance/Bills.vue'
import CreditMemos from '../../../src/pages/finance/CreditMemos.vue'
import Expenses from '../../../src/pages/finance/Expenses.vue'
import Payments from '../../../src/pages/finance/Payments.vue'
import JournalEntries from '../../../src/pages/finance/JournalEntries.vue'
import OfferPage from '../../../src/pages/finance/components/OfferPage.vue'

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

type PageVm = {
  crud: { openView: (row: { id: string }) => void }
  viewed: { id: string } | null
  applications?: Array<{ id: string }>
}

const pages: Array<[string, Component, Record<string, unknown>]> = [
  ['Invoices', Invoices, {}],
  ['Bills', Bills, {}],
  ['CreditMemos', CreditMemos, {}],
  ['Expenses', Expenses, {}],
  ['Payments', Payments, {}],
  ['JournalEntries', JournalEntries, {}],
  ['Estimates', OfferPage, { kind: 'estimate' }],
]

describe('finance detail drawers keep the last record opened', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    pending.clear()
    getById.mockClear()
    applications.mockClear()
  })

  it.each(pages)('★ %s: a late answer for the row opened before does not replace the open one', async (_name, page, props) => {
    const wrapper = mount(page, { props, global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as PageVm

    vm.crud.openView({ id: 'A' })
    await flushPromises()
    vm.crud.openView({ id: 'B' })
    await flushPromises()

    pending.get('B')!({ id: 'B', lines: [] })
    await flushPromises()
    pending.get('A')!({ id: 'A', lines: [] })
    await flushPromises()

    expect(vm.viewed?.id).toBe('B')
    if (vm.applications) expect(vm.applications.map((a) => a.id)).toEqual(['app-B'])
  })
})
