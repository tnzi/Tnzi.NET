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
const settlementsPay = vi.fn(async () => ({ payments: [{ id: 'pay1' }], applications: [] }))

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
      settlements: { applications: listOnly, openDocuments: listOnly, apply: vi.fn(), unapply: vi.fn(), pay: settlementsPay },
    }),
  }
})

import Page from '../../../src/pages/finance/Bills.vue'
import { applyDocumentSearch } from './document-search.helper'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

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

  /**
   * Paying bills creates the payments AND posts and applies them; the backend gates
   * it on finance.document.create plus finance.document.update, so a drafting role
   * (create only) must not be offered either way in.
   */
  it('offers Pay Bills only to a role that may also post', async () => {
    const auth = useAdminAuthStore()
    const openBill = { id: 'd1', status: 'Posted', total: 100, appliedTotal: 0 }

    auth.setUserInfo({ id: 'clerk', username: 'clerk', roles: [], permissions: ['finance.document.view', 'finance.document.create'] } as never)
    const drafting = mount(Page, { global: { stubs } })
    await flushPromises()
    const draftingVm = drafting.vm as unknown as { canPayBills: boolean; rowActions: Array<{ key: string; show?: (row: unknown) => boolean }> }
    expect(draftingVm.canPayBills).toBe(false)
    expect(draftingVm.rowActions.find((a) => a.key === 'pay')!.show!(openBill)).toBe(false)
    drafting.unmount()

    auth.setUserInfo({ id: 'mgr', username: 'mgr', roles: [], permissions: ['finance.document.view', 'finance.document.create', 'finance.document.update'] } as never)
    const posting = mount(Page, { global: { stubs } })
    await flushPromises()
    const postingVm = posting.vm as unknown as { canPayBills: boolean; rowActions: Array<{ key: string; show?: (row: unknown) => boolean }> }
    expect(postingVm.canPayBills).toBe(true)
    expect(postingVm.rowActions.find((a) => a.key === 'pay')!.show!(openBill)).toBe(true)
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })
  // The list endpoint binds its filters by name: bills filter by vendor (`vendorId`), and a
  // key it does not declare is dropped without an error, so the list would
  // claim to be filtered while showing every party. The date goes out as the
  // picked calendar day; the picker's epoch number fails model binding.
  it('the advanced search filters by the party key the list endpoint binds, and a calendar date', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const filters = await applyDocumentSearch(wrapper)
    expect(filters).toEqual({ dateFrom: '2026-01-15', vendorId: 'party-1' })
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

  /**
   * The picker derives each bill's outstanding balance in the browser. In
   * doubles `50.3 - 20.1` is `30.199999999999996`; prefilled by "Fill all" and
   * sent as-is, that allocation leaves the bill a hair short of Paid forever.
   */
  it('prefills and sends pay-bills allocations in whole cents', async () => {
    settlementsPay.mockClear()
    fetchList.mockImplementation(async (query: { filters?: { status?: string } }) => {
      const items = query.filters?.status === 'PartiallyPaid'
        ? [{ id: 'b1', number: 'BILL-1', status: 'PartiallyPaid', docDate: '2026-07-01', currency: 'USD', total: 50.3, appliedTotal: 20.1 }]
        : query.filters?.status === 'Posted'
          ? [{ id: 'b2', number: 'BILL-2', status: 'Posted', docDate: '2026-07-01', currency: 'USD', total: 0.3, appliedTotal: 0.1 }]
          : []
      return { items, totalCount: items.length, pageIndex: 1, pageSize: 100, totalPages: 1, hasPreviousPage: false, hasNextPage: false }
    })
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      payDetail: { open: (a: string) => Promise<void> }
      openBills: { id: string; outstanding: number }[]
      payForm: { fundsAccountId: string | null }
      payAllocations: Record<string, number | null>
      payTotal: number
      fillAll: () => void
      submitPay: () => Promise<void>
    }
    await vm.payDetail.open('create')
    await flushPromises()
    await flushPromises()

    expect(vm.openBills.find((b) => b.id === 'b1')?.outstanding).toBe(30.2)
    expect(vm.openBills.find((b) => b.id === 'b2')?.outstanding).toBe(0.2)

    vm.fillAll()
    // A value the operator arrives at by arithmetic carries the same float tail.
    vm.payAllocations.b2 = 0.1 + 0.2 - 0.1
    vm.payForm.fundsAccountId = 'bank'
    expect(vm.payTotal).toBe(30.4)
    await vm.submitPay()
    await flushPromises()

    expect(settlementsPay).toHaveBeenCalledTimes(1)
    const sent = (settlementsPay.mock.calls[0] as unknown as [{ targets: { docId: string; amount: number }[] }])[0]
    expect([...sent.targets].sort((a, b) => a.docId.localeCompare(b.docId))).toEqual([
      expect.objectContaining({ docId: 'b1', amount: 30.2 }),
      expect.objectContaining({ docId: 'b2', amount: 0.2 }),
    ])

    // The running total the submit button is gated on is a derived sum too.
    vm.payAllocations.b1 = 0.1
    vm.payAllocations.b2 = 0.2
    expect(vm.payTotal).toBe(0.3)
    fetchList.mockReset()
    fetchList.mockImplementation(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }))
  })

  // Open bills of every currency share one picker (the backend pays them per
  // vendor and currency). The running total added them all up and showed one
  // currency-less figure.
  it('★ the pay-bills total lists each currency on its own', async () => {
    fetchList.mockImplementation(async (query: { filters?: { status?: string } }) => {
      const items = query.filters?.status === 'Posted'
        ? [
            { id: 'u1', number: 'BILL-U1', status: 'Posted', docDate: '2026-07-01', currency: 'USD', total: 1000, appliedTotal: 0 },
            { id: 'c1', number: 'BILL-C1', status: 'Posted', docDate: '2026-07-01', currency: 'CAD', total: 350, appliedTotal: 0 },
          ]
        : []
      return { items, totalCount: items.length, pageIndex: 1, pageSize: 100, totalPages: 1, hasPreviousPage: false, hasNextPage: false }
    })
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      payDetail: { open: (a: string) => Promise<void> }
      fillAll: () => void
      payTotalText: string
    }
    await vm.payDetail.open('create')
    await flushPromises()
    await flushPromises()
    vm.fillAll()

    expect(vm.payTotalText).toBe('350.00 CAD · 1,000.00 USD')
    fetchList.mockReset()
    fetchList.mockImplementation(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }))
  })

  /**
   * A draft update replaces the whole bill. A bill created through the API
   * with a manual rate and the vendor's printed tax as a line override must
   * keep both when a reviewer only fixes the memo in the editor.
   */
  it('saving an edited draft sends back the rate and tax overrides the editor does not show', async () => {
    docSection.getById.mockResolvedValueOnce({
      id: 'b9', status: 'Draft', vendorId: 'v1', docDate: '2026-07-01', currency: 'USD', exchangeRate: 1.35, memo: 'From the portal',
      lines: [{ id: 'l1', lineNumber: 1, itemId: null, description: 'Consulting', accountId: 'a1', quantity: 1, unitPrice: 100, amount: 100, taxCodeId: 'hst', taxAmount: 12.99 }],
    } as never)
    docSection.updateDraft.mockClear().mockResolvedValueOnce({ id: 'b9' } as never)
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as { openEdit: (row: { id: string }) => void }
    vm.openEdit({ id: 'b9' })
    await flushPromises()
    const editor = wrapper.findComponent({ name: 'DocumentEditor' })
    const editorVm = editor.vm as unknown as { memo: string; save: (post: boolean) => Promise<void> }
    editorVm.memo = 'Checked against the PDF'
    await editorVm.save(false)

    expect(docSection.updateDraft).toHaveBeenCalledWith('b9', expect.objectContaining({
      memo: 'Checked against the PDF',
      exchangeRate: 1.35,
      lines: [expect.objectContaining({ taxCodeId: 'hst', taxAmount: 12.99 })],
    }))
    wrapper.unmount()
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
