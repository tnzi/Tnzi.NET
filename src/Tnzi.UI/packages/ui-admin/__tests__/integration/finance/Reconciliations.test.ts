import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bank Reconciliation page - TCrudPage list + worksheet drawer with
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

const getById = vi.fn(async (id: string = 'r1') => ({
  id, accountId: 'a1', accountName: '1120 Bank', statementDate: '2026-03-31',
  statementEndingBalance: 600, status: 'Draft', lineCount: 0, clearedBalance: 0, difference: 600,
}))

type WorksheetLine = { journalLineId: string; journalEntryId: string; entryNumber: string; postingDate: string; memo: string; debit: number; credit: number; isSelected: boolean }
type WorksheetDto = { reconciliationId: string; statementEndingBalance: number; clearedBalance: number; difference: number; lines: WorksheetLine[] }

const worksheetFor = (id: string): WorksheetDto => ({
  reconciliationId: id,
  statementEndingBalance: 600,
  clearedBalance: 0,
  difference: 600,
  lines: [
    { journalLineId: 'l1', journalEntryId: 'e1', entryNumber: 'JE-000001', postingDate: '2026-03-05', memo: 'in', debit: 500, credit: 0, isSelected: false },
    { journalLineId: 'l2', journalEntryId: 'e2', entryNumber: 'JE-000002', postingDate: '2026-03-10', memo: 'in', debit: 200, credit: 0, isSelected: false },
    { journalLineId: 'l3', journalEntryId: 'e3', entryNumber: 'JE-000003', postingDate: '2026-03-15', memo: 'out', debit: 0, credit: 100, isSelected: false },
  ],
})

const worksheet = vi.fn(async (id: string): Promise<WorksheetDto> => worksheetFor(id))
// Writes the selection as the reconciliation's WHOLE set of cleared lines.
const setLines = vi.fn(async (id: string, ids: string[]): Promise<WorksheetDto> => {
  const ws = worksheetFor(id)
  return { ...ws, lines: ws.lines.map((l) => ({ ...l, isSelected: ids.includes(l.journalLineId) })) }
})
const complete = vi.fn(async () => ({}))

const reconSection = {
  fetch: fetchList,
  getById,
  create: vi.fn(),
  update: vi.fn(),
  delete: vi.fn(),
  worksheet,
  setLines,
  complete,
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
  worksheetDetail: { open: (action: 'view', id: string) => Promise<void>; close: () => void }
  worksheet: { reconciliationId?: string; lines: unknown[] } | null
  selectedIds: string[]
  liveCleared: number
  liveDifference: number
  saveLines: () => Promise<void>
  completeFromWorksheet: () => Promise<void>
}

describe('Finance Reconciliations page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    worksheet.mockReset()
    worksheet.mockImplementation(async (id: string) => worksheetFor(id))
    setLines.mockClear()
    complete.mockClear()
  })

  // The reconciliation list endpoint has no keyword, so a search box would be
  // shown and silently ignored: typing a reference returned every row.
  it('★ shows no keyword search box', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.t-list-shell__search').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(false)
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

  describe('Save / Complete write the selection as the WHOLE cleared set', () => {
    function deferred<T>() {
      let resolve: (value: T) => void = () => {}
      const promise = new Promise<T>((res) => { resolve = res })
      return { promise, resolve }
    }

    function button(wrapper: ReturnType<typeof mount>, label: string) {
      return wrapper.findAll('button').find((b) => b.text().trim() === label)!
    }

    it('saves the selection onto the reconciliation whose worksheet was read', async () => {
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as ReconciliationsVm
      await vm.worksheetDetail.open('view', 'r1')
      await flushPromises()

      vm.selectedIds = ['l1']
      await button(wrapper, 'Save Selection').trigger('click')
      await flushPromises()
      expect(setLines).toHaveBeenCalledWith('r1', ['l1'])
    })

    it('a failed worksheet read offers neither Save nor Complete (an empty selection would clear every line)', async () => {
      worksheet.mockImplementation(async () => {
        throw new Error('Worksheet unavailable')
      })
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as ReconciliationsVm
      await vm.worksheetDetail.open('view', 'r1')
      await flushPromises()

      expect(button(wrapper, 'Save Selection').attributes('disabled')).toBeDefined()
      // With nothing loaded the difference bar reads 0, which is what enabled Complete.
      expect(button(wrapper, 'Complete').attributes('disabled')).toBeDefined()
      await vm.saveLines()
      await vm.completeFromWorksheet()
      expect(setLines).not.toHaveBeenCalled()
      expect(complete).not.toHaveBeenCalled()
    })

    it('offers no Save while the worksheet is still being read', async () => {
      const pending = deferred<WorksheetDto>()
      worksheet.mockImplementation(() => pending.promise)
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as ReconciliationsVm
      await vm.worksheetDetail.open('view', 'r1')
      await flushPromises()

      await vm.saveLines()
      expect(setLines).not.toHaveBeenCalled()
      pending.resolve(worksheetFor('r1'))
      await flushPromises()
      expect(button(wrapper, 'Save Selection').attributes('disabled')).toBeUndefined()
    })

    it('a late worksheet for the reconciliation opened before is neither shown nor saved onto the next', async () => {
      const forFirst = deferred<WorksheetDto>()
      worksheet.mockImplementation((id: string) => (id === 'r1' ? forFirst.promise : Promise.resolve({ ...worksheetFor('r2'), lines: worksheetFor('r2').lines.map((l) => ({ ...l, isSelected: l.journalLineId === 'l3' })) })))
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      const vm = wrapper.vm as unknown as ReconciliationsVm

      await vm.worksheetDetail.open('view', 'r1')
      await flushPromises()
      vm.worksheetDetail.close()
      await flushPromises()
      await vm.worksheetDetail.open('view', 'r2')
      await flushPromises()
      forFirst.resolve({ ...worksheetFor('r1'), lines: worksheetFor('r1').lines.map((l) => ({ ...l, isSelected: true })) })
      await flushPromises()

      expect(vm.worksheet?.reconciliationId).toBe('r2')
      expect(vm.selectedIds).toEqual(['l3'])
      await vm.saveLines()
      expect(setLines).toHaveBeenCalledTimes(1)
      expect(setLines).toHaveBeenCalledWith('r2', ['l3'])
    })
  })
})
