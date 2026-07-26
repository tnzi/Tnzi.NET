import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Statements & collections - the worklist, and the statement it hands off to.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/statements', fullPath: '/admin/finance/statements', hash: '', name: 'finance.statements', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const buckets = { current: 0, days1To30: 0, days31To60: 0, days61To90: 0, over90: 700, total: 700 }

const dunning = vi.fn(async () => [
  { partyId: 'c1', partyName: 'Very Late Ltd', openBalance: 700, overdue: 700, oldestOverdueDays: 120, level: 'FinalNotice', buckets },
  { partyId: 'c2', partyName: 'Slightly Late Ltd', openBalance: 400, overdue: 400, oldestOverdueDays: 40, level: 'Overdue', buckets },
])

const getStatement = vi.fn(async () => ({
  partyId: 'c1',
  partyName: 'Very Late Ltd',
  partyType: 0,
  style: 'OpenItem',
  currency: 'USD',
  periodFrom: '2026-07-01',
  periodTo: '2026-07-25',
  openingBalance: 0,
  closingBalance: 700,
  overdue: 700,
  dunningLevel: 'FinalNotice',
  buckets,
  lines: [
    { docDate: '2026-03-01', dueDate: '2026-03-31', docType: 'Invoice', docId: 'i1', number: 'INV-000001', charge: 700, payment: 0, outstanding: 700, overdueDays: 116, balance: 700 },
  ],
}))

const download = vi.fn(async () => new Blob(['<html></html>'], { type: 'text/html' }))

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    FinancePartyType: original.FinancePartyType,
    createFinanceBridge: () => ({
      customers: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })) },
      vendors: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })) },
      statements: { get: getStatement, download, dunning },
    }),
  }
})

import Page from '../../../src/pages/finance/Statements.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Select: { name: 'Select', template: '<select />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  Tag: { name: 'Tag', template: '<span><slot /></span>' },
  RadioGroup: { name: 'RadioGroup', template: '<div><slot /></div>' },
  RadioButton: { name: 'RadioButton', template: '<label><slot /></label>' },
  Alert: { name: 'Alert', template: '<div><slot /></div>' },
}

interface StatementsVm {
  section: string
  partyId: string | null
  openStatementFor: (id: string) => void
  loadStatement: () => Promise<void>
  candidates: Array<{ partyId: string; level: string }>
  renderError: string
}

describe('Finance Statements page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    dunning.mockClear()
    getStatement.mockClear()
    download.mockClear()
  })

  it('loads the collections worklist on mount', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    expect(dunning).toHaveBeenCalled()
    expect((wrapper.vm as unknown as StatementsVm).candidates.length).toBe(2)
    // Worst first - the point of the list.
    expect((wrapper.vm as unknown as StatementsVm).candidates[0]!.level).toBe('FinalNotice')
  })

  /**
   * A worklist that only lists names makes you go elsewhere to act; picking a
   * row must land on that party's statement, not just switch tabs.
   */
  it('hands a worklist row off to its statement', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as StatementsVm
    vm.openStatementFor('c1')
    await flushPromises()

    expect(vm.section).toBe('statement')
    expect(vm.partyId).toBe('c1')
    expect(getStatement).toHaveBeenCalled()
  })

  it('does not fetch a statement before a party is picked', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    await (wrapper.vm as unknown as StatementsVm).loadStatement()
    expect(getStatement).not.toHaveBeenCalled()
  })
})
