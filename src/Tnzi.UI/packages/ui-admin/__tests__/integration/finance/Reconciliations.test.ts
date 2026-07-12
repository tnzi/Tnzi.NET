import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bank Reconciliation page — TCrudPage list + worksheet drawer with
 * cleared-line selection and a live difference bar.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/reconciliations', fullPath: '/admin/finance/reconciliations', hash: '', name: 'finance.reconciliations', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchList = vi.fn(async () => ({
  items: [
    { id: 'r1', accountId: 'a1', accountName: '1120 Bank', statementDate: '2026-03-31', statementEndingBalance: 600, status: 'Draft', lineCount: 0, clearedBalance: 0, difference: 600 },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const getById = vi.fn(async () => ({
  id: 'r1', accountId: 'a1', accountName: '1120 Bank', statementDate: '2026-03-31',
  statementEndingBalance: 600, status: 'Draft', lineCount: 0, clearedBalance: 0, difference: 600,
}))

const worksheet = vi.fn(async () => ({
  reconciliationId: 'r1',
  statementEndingBalance: 600,
  clearedBalance: 0,
  difference: 600,
  lines: [
    { journalLineId: 'l1', journalEntryId: 'e1', entryNumber: 'JE-000001', postingDate: '2026-03-05', memo: 'in', debit: 500, credit: 0, isSelected: false },
    { journalLineId: 'l2', journalEntryId: 'e2', entryNumber: 'JE-000002', postingDate: '2026-03-10', memo: 'in', debit: 200, credit: 0, isSelected: false },
    { journalLineId: 'l3', journalEntryId: 'e3', entryNumber: 'JE-000003', postingDate: '2026-03-15', memo: 'out', debit: 0, credit: 100, isSelected: false },
  ],
}))

const reconSection = {
  fetch: fetchList,
  getById,
  create: vi.fn(),
  update: vi.fn(),
  delete: vi.fn(),
  worksheet,
  setLines: vi.fn(),
  complete: vi.fn(),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    FinanceDocumentStatus: original.FinanceDocumentStatus,
    ReconciliationStatus: original.ReconciliationStatus,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      reconciliations: reconSection,
    }),
  }
})

import Page from '../../../src/pages/finance/Reconciliations.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
}

interface ReconciliationsVm {
  worksheetDetail: { open: (action: 'view', id: string) => void }
  worksheet: { lines: unknown[] } | null
  selectedIds: string[]
  liveCleared: number
  liveDifference: number
}

describe('Finance Reconciliations page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    worksheet.mockClear()
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })

  it('loads the worksheet and tracks the live difference as lines are selected', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReconciliationsVm
    vm.worksheetDetail.open('view', 'r1')
    await flushPromises()

    expect(worksheet).toHaveBeenCalledTimes(1)
    expect(vm.worksheet!.lines.length).toBe(3)
    expect(vm.liveDifference).toBe(600)

    // 勾选全部三行（500 + 200 - 100 = 600）→ 差额归零
    vm.selectedIds = ['l1', 'l2', 'l3']
    await flushPromises()
    expect(vm.liveCleared).toBe(600)
    expect(vm.liveDifference).toBe(0)
  })
})
