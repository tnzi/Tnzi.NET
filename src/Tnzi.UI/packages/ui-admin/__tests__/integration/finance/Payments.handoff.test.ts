import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Payments page - TCrudPage with settlement apply panel and applications detail.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// A reactive route the test can move after mount, the way a page kept alive in
// its tab sees a hand-off: as a change of its route, not as a fresh mount.
const routing = vi.hoisted(() => ({ route: null as unknown as { query: Record<string, unknown> }, replace: null as unknown as (to: unknown) => Promise<void> }))
vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  routing.route = reactive({ query: {}, params: {}, path: '/admin/finance/payments', fullPath: '/admin/finance/payments', hash: '', name: 'finance.payments', meta: {} }) as never
  routing.replace = vi.fn(async (to: unknown) => {
    routing.route.query = { ...((to as { query?: Record<string, unknown> }).query ?? {}) }
  })
  return {
    useRoute: () => routing.route,
    useRouter: () => ({ push: vi.fn(), replace: routing.replace, back: vi.fn() }),
  }
})

const fetchList = vi.fn(async () => ({ items: [{ id: 'd1', number: 'PMT-000001', status: 'Posted', direction: 'Inbound', partyType: 'Customer', partyId: 'c1', docDate: '2026-07-01', currency: 'USD', amount: 100, appliedTotal: 0 }], totalCount: 1, pageIndex: 1, pageSize: 20 }))

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

import Page from '../../../src/pages/finance/Payments.vue'

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

type PaymentsVm = {
  crud: { formModal: { visible: { value: boolean }; formData: { value: Record<string, unknown> | null } } }
}

/**
 * The `?party=&direction=` hand-off from a customer or vendor page opens a
 * pre-filled payment. It was read once, on mount; with the page kept alive in
 * its tab, a hand-off into the already-open Payments tab arrives as a route
 * change and opened nothing.
 */
describe('Payments - the party hand-off', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    routing.route.query = {}
    vi.mocked(routing.replace).mockClear()
  })

  it('a hand-off into the open page opens the pre-filled draft and consumes the query', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as PaymentsVm
    expect(vm.crud.formModal.visible.value).toBe(false)

    routing.route.query = { party: 'c9', direction: 'Outbound', keep: 'me' }
    await flushPromises()

    expect(vm.crud.formModal.visible.value).toBe(true)
    expect(vm.crud.formModal.formData.value).toMatchObject({ partyId: 'c9', direction: 'Outbound' })
    expect(routing.route.query).toEqual({ keep: 'me' })
    wrapper.unmount()
  })

  it('a hand-off present at mount still opens the draft once', async () => {
    routing.route.query = { party: 'c1' }
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as PaymentsVm
    expect(vm.crud.formModal.formData.value).toMatchObject({ partyId: 'c1', direction: 'Inbound' })
    expect(routing.replace).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })
})
