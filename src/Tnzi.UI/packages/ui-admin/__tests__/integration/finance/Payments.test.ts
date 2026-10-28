import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Payments page - TCrudPage with settlement apply panel and applications detail.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/payments', fullPath: '/admin/finance/payments', hash: '', name: 'finance.payments', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

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

describe('Finance Payments page', () => {
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
  // The list endpoint binds its filters by name: payments filter by either party (`partyId`), and a
  // key it does not declare is dropped without an error, so the list would
  // claim to be filtered while showing every party. The date goes out as the
  // picked calendar day; the picker's epoch number fails model binding.
  it('the advanced search filters by the party key the list endpoint binds, and a calendar date', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const filters = await applyDocumentSearch(wrapper)
    expect(filters).toEqual({ dateFrom: '2026-01-15', partyId: 'party-1' })
  })

  /**
   * The payment form has no exchange-rate field, and a draft update replaces
   * the payment (`ExchangeRate = input ?? 0`). A manual rate set through the
   * API must survive an edit made for something else, and must NOT follow the
   * payment into a different currency.
   */
  describe('editing a foreign-currency draft', () => {
    type CrudVm = {
      formModal: { formData: { value: Record<string, unknown> | null } }
      openEdit: (row: unknown) => void
      submit: () => Promise<unknown>
    }
    const draft = {
      id: 'p7', status: 'Draft', direction: 'Outbound', partyType: 'Vendor', partyId: 'v1',
      docDate: '2026-07-01', currency: 'USD', exchangeRate: 1.3601, amount: 250, appliedTotal: 0, memo: null,
    }

    it('sends the stored rate back when the currency is unchanged', async () => {
      docSection.updateDraft.mockClear()
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const crud = (wrapper.vm as unknown as { crud: CrudVm }).crud

      crud.openEdit(draft)
      crud.formModal.formData.value!.memo = 'Wire ref 8812'
      await crud.submit()

      expect(docSection.updateDraft).toHaveBeenCalledWith('p7', expect.objectContaining({ memo: 'Wire ref 8812', currency: 'USD', exchangeRate: 1.3601 }))
      wrapper.unmount()
    })

    it('leaves the rate to the rate table once the currency changes', async () => {
      docSection.updateDraft.mockClear()
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const crud = (wrapper.vm as unknown as { crud: CrudVm }).crud

      crud.openEdit(draft)
      crud.formModal.formData.value!.currency = 'eur'
      await crud.submit()

      expect(docSection.updateDraft).toHaveBeenCalledWith('p7', expect.objectContaining({ currency: 'EUR', exchangeRate: null }))
      wrapper.unmount()
    })
  })
})
