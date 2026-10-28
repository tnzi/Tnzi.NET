import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Taxes page - TContentPage with agencies/rates/codes tabs (deep-linkable).
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/taxes', fullPath: '/admin/finance/taxes', hash: '', name: 'finance.taxes', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchList = vi.fn(async () => ({ items: [], totalCount: 1, pageIndex: 1, pageSize: 20 }))

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
const rateList = vi.fn(async (): Promise<unknown[]> => [])
const updateCode = vi.fn(async () => ({}))

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
        agencies: listOnly, rates: rateList, codes: listOnly,
        createAgency: vi.fn(), updateAgency: vi.fn(), deleteAgency: vi.fn(),
        createRate: vi.fn(), updateRate: vi.fn(), deleteRate: vi.fn(),
        createCode: vi.fn(), updateCode, deleteCode: vi.fn(),
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

import Page from '../../../src/pages/finance/Taxes.vue'

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

describe('Finance Taxes page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    listOnly.mockClear()
    rateList.mockReset()
    rateList.mockImplementation(async () => [])
  })

  // The three tabs load their whole list from endpoints that take no keyword:
  // the search box was shown and ignored.
  it('★ the search box filters a loaded tax list by its name and agency', async () => {
    rateList.mockImplementation(async () => [
      { id: 'r1', agencyId: 'cra', agencyName: 'CRA', name: 'GST', rate: 5, isActive: true },
      { id: 'r2', agencyId: 'rq', agencyName: 'Revenu Quebec', name: 'QST', rate: 9.975, isActive: true },
    ])
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const crud = (wrapper.vm as unknown as { rateCrud: { items: { value: Array<{ id: string }> }; setSearch: (s: string) => void; refresh: () => Promise<void> } }).rateCrud
    expect(crud.items.value).toHaveLength(2)

    crud.setSearch('qst')
    await crud.refresh()
    await flushPromises()
    expect(crud.items.value.map((r) => r.id)).toEqual(['r2'])

    crud.setSearch('cra')
    await crud.refresh()
    await flushPromises()
    expect(crud.items.value.map((r) => r.id)).toEqual(['r1'])
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(listOnly.mock.calls.length).toBeGreaterThan(0)
  })

  /**
   * The code form edits components as a list of rate ids and has no compound
   * switch, while the backend rebuilds every component from the payload. A code
   * configured through the API as compound (second rate charged on amount +
   * first tax) lost the flag the first time anyone renamed it here, and every
   * later invoice computed the wrong tax with no warning.
   */
  describe('editing a compound tax code', () => {
    type CrudVm = {
      formModal: { formData: { value: Record<string, unknown> | null } }
      openEdit: (row: unknown) => void
      submit: () => Promise<unknown>
    }
    type PageVm = {
      codeCrud: CrudVm
      fieldRenderers: Record<string, (ctx: { value: unknown; onUpdate: (v: unknown) => void; readonly: boolean }) => { props: Record<string, (v: unknown) => void> }>
    }
    const compoundCode = {
      id: 'tc1',
      name: 'GST + QST',
      description: null,
      isActive: true,
      isRecoverable: true,
      components: [
        { taxRateId: 'gst', rateName: 'GST', rate: 5, order: 1, isCompound: false },
        { taxRateId: 'qst', rateName: 'QST', rate: 9.975, order: 2, isCompound: true },
      ],
    }

    beforeEach(() => updateCode.mockClear())

    it('keeps every component compound flag when only the name changes', async () => {
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as PageVm

      vm.codeCrud.openEdit(compoundCode)
      vm.codeCrud.formModal.formData.value!.name = 'GST + QST (renamed)'
      await vm.codeCrud.submit()

      expect(updateCode).toHaveBeenCalledTimes(1)
      expect((updateCode.mock.calls[0] as unknown[])[1]).toMatchObject({
        name: 'GST + QST (renamed)',
        components: [
          { taxRateId: 'gst', order: 1, isCompound: false },
          { taxRateId: 'qst', order: 2, isCompound: true },
        ],
      })
      wrapper.unmount()
    })

    it('keeps the flag on the rates that stay when the selection changes', async () => {
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as PageVm

      vm.codeCrud.openEdit(compoundCode)
      const form = vm.codeCrud.formModal.formData.value!
      const select = vm.fieldRenderers['finance-tax-components']({
        value: form.components,
        readonly: false,
        onUpdate: (v) => { form.components = v },
      })
      // The operator adds a third rate to the multi-select.
      select.props['onUpdate:value'](['gst', 'qst', 'pst'])
      await vm.codeCrud.submit()

      expect((updateCode.mock.calls[0] as unknown[])[1]).toMatchObject({
        components: [
          { taxRateId: 'gst', order: 1, isCompound: false },
          { taxRateId: 'qst', order: 2, isCompound: true },
          { taxRateId: 'pst', order: 3, isCompound: false },
        ],
      })
      wrapper.unmount()
    })
  })
})
