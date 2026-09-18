import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bills page - read-only list + #detail drawer + useDetail line editor.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Mutable so a test can arrive with a hand-off in the URL.
const routeQuery = vi.hoisted(() => ({ current: {} as Record<string, string> }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery.current, params: {}, path: '/admin/finance/bills', fullPath: '/admin/finance/bills', hash: '', name: 'finance.bills', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchList = vi.fn(async () => ({ items: [{ id: 'd1', number: 'BILL-000001', status: 'Posted', docDate: '2026-07-01', currency: 'USD', total: 100, appliedTotal: 0 }], totalCount: 1, pageIndex: 1, pageSize: 20 }))

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

import Page from '../../../src/pages/finance/Bills.vue'

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

describe('Finance Bills page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    listOnly.mockClear()
    routeQuery.current = {}
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })

  // The vendor work surface hands off with `?entry=new&party=<id>`. `new` is
  // useDetail's create token - spelling it `create` silently opens nothing,
  // which typecheck and a push-payload assertion both wave through.
  /**
   * The pay-bills dialog builds its picker from the server-paged bills list,
   * which clamps `pageSize` to 100 silently. Two single pages of 100 meant a
   * vendor-heavy tenant could not allocate a payment to bills past the 100th
   * per status: the row simply was not listed, no error anywhere - the shape
   * every other finance picker was cured of with `fetchAllPages`.
   */
  it('the pay-bills picker loads every open bill past the server page clamp', async () => {
    const posted = Array.from({ length: 130 }, (_, i) => ({
      id: `p${i + 1}`, number: `BILL-P${i + 1}`, status: 'Posted', docDate: '2026-07-01', currency: 'USD', total: 10, appliedTotal: 0,
    }))
    const partial = Array.from({ length: 105 }, (_, i) => ({
      id: `q${i + 1}`, number: `BILL-Q${i + 1}`, status: 'PartiallyPaid', docDate: '2026-07-01', currency: 'USD', total: 10, appliedTotal: 4,
    }))
    fetchList.mockImplementation(async (query: { pageIndex: number; pageSize: number; filters?: { status?: string } }) => {
      const rows = query.filters?.status === 'Posted' ? posted : query.filters?.status === 'PartiallyPaid' ? partial : []
      const size = Math.min(query.pageSize, 100)
      const start = (query.pageIndex - 1) * size
      const items = rows.slice(start, start + size)
      const totalPages = Math.ceil(rows.length / size)
      return { items, totalCount: rows.length, pageIndex: query.pageIndex, pageSize: size, totalPages, hasPreviousPage: query.pageIndex > 1, hasNextPage: query.pageIndex < totalPages }
    })
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as { payDetail: { open: (a: string) => Promise<void> }; openBills: { id: string }[] }
    await vm.payDetail.open('create')
    await flushPromises()
    await flushPromises()

    expect(vm.openBills).toHaveLength(235)
    expect(vm.openBills.some((b) => b.id === 'p130')).toBe(true)
    expect(vm.openBills.some((b) => b.id === 'q105')).toBe(true)
    fetchList.mockReset()
    fetchList.mockImplementation(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }))
  })

  it('opens a pre-filled draft from a vendor hand-off', async () => {
    routeQuery.current = { entry: 'new', party: 'p9' }
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const editor = wrapper.findComponent({ name: 'DocumentEditor' })
    expect(editor.exists()).toBe(true)
    expect(editor.props('entry')).toMatchObject({ partyId: 'p9' })
  })
})
