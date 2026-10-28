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

const accountTree = vi.fn(async (): Promise<unknown[]> => [])

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
    CashFlowActivity: original.CashFlowActivity,
    createFinanceBridge: () => ({
      accounts: { tree: accountTree },
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

  /**
   * The transfer form shows a cross-currency target rate but not the source
   * rate, and a draft update replaces the transfer. A manual source rate set
   * through the API must survive an unrelated edit, and must not follow the
   * transfer into a different currency.
   */
  describe('editing a foreign-currency draft', () => {
    type CrudVm = {
      formModal: { formData: { value: Record<string, unknown> | null } }
      openEdit: (row: unknown) => void
      submit: () => Promise<unknown>
    }
    const draft = {
      id: 't7', status: 'Draft', fromAccountId: 'usd-bank', toAccountId: 'usd-savings', transferDate: '2026-07-01',
      amount: 500, currency: 'USD', exchangeRate: 1.3577, targetCurrency: null, targetAmount: null, targetExchangeRate: 0, reference: null, memo: null,
    }

    it('sends the stored source rate back when the currency is unchanged', async () => {
      transferSection.updateDraft.mockClear()
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const crud = (wrapper.vm as unknown as { crud: CrudVm }).crud

      crud.openEdit(draft)
      crud.formModal.formData.value!.reference = 'Sweep'
      await crud.submit()

      expect(transferSection.updateDraft).toHaveBeenCalledWith('t7', expect.objectContaining({ reference: 'Sweep', currency: 'USD', exchangeRate: 1.3577 }))
      wrapper.unmount()
    })

    it('leaves the rate to the rate table once the currency changes', async () => {
      transferSection.updateDraft.mockClear()
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const crud = (wrapper.vm as unknown as { crud: CrudVm }).crud

      crud.openEdit(draft)
      crud.formModal.formData.value!.currency = 'EUR'
      await crud.submit()

      expect(transferSection.updateDraft).toHaveBeenCalledWith('t7', expect.objectContaining({ currency: 'EUR', exchangeRate: null }))
      wrapper.unmount()
    })
  })

  // The transfer list endpoint has no keyword, so a search box would be
  // shown and silently ignored: typing a reference returned every row.
  it('★ shows no keyword search box', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.t-list-shell__search').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(false)
  })

  // The server takes only funds (CashEquivalent) accounts on either side of a
  // transfer; offering every leaf let the form present choices it always refused.
  it('★ offers only funds accounts as the transfer sides', async () => {
    const leaf = (id: string, cashFlowActivity: string) => ({ id, code: id, name: id, isGroup: false, isActive: true, cashFlowActivity, children: [] })
    accountTree.mockResolvedValue([
      { id: 'assets', code: '1000', name: 'Assets', isGroup: true, isActive: true, children: [leaf('bank', 'CashEquivalent'), leaf('ar', 'Operating')] },
      leaf('sales', 'Operating'),
    ])
    try {
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as {
        crud: { openCreate: () => void }
        fieldRenderers: Record<string, (ctx: unknown) => { props: { options: Array<{ value: string }> } }>
      }
      vm.crud.openCreate()
      await flushPromises()

      const select = vm.fieldRenderers['finance-account']!({ value: null, readonly: false, onUpdate: () => {} })
      expect(select.props.options.map((o) => o.value)).toEqual(['bank'])
      wrapper.unmount()
    } finally {
      accountTree.mockReset().mockResolvedValue([])
    }
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
