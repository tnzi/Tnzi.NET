import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Financial Reports page — TContentPage with four report tabs (trial balance,
 * balance sheet, P&L, general ledger) that aggregate on demand.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/reports', fullPath: '/admin/finance/reports', hash: '', name: 'finance.reports', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const trialBalance = vi.fn(async () => ({
  from: '2026-01-01',
  to: '2026-12-31',
  baseCurrency: 'USD',
  rows: [
    { accountId: 'a1', code: '1200', name: 'Accounts Receivable', rootType: 'Asset', openingBalance: 0, periodDebit: 1000, periodCredit: 0, closingBalance: 1000 },
  ],
  totalOpeningBalance: 0,
  totalPeriodDebit: 1000,
  totalPeriodCredit: 1000,
  totalClosingBalance: 0,
}))
const tree = vi.fn(async () => [])

vi.mock('../../../src/services/bridges/finance-bridge', () => ({
  createFinanceBridge: () => ({
    accounts: { tree },
    reports: {
      trialBalance,
      balanceSheet: vi.fn(),
      profitAndLoss: vi.fn(),
      generalLedger: vi.fn(),
    },
  }),
}))

import Reports from '../../../src/pages/finance/Reports.vue'

const stubs = {
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot /></div>' },
  Tabs: { name: 'Tabs', template: '<div class="n-tabs-stub"><slot /></div>' },
  TabPane: { name: 'TabPane', template: '<div class="n-tab-pane-stub"><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Select: { name: 'Select', template: '<select class="n-select-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
}

interface ReportsVm {
  runTrialBalance: () => Promise<void>
  tb: unknown
}

describe('Finance Reports page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    trialBalance.mockClear()
    tree.mockClear()
  })

  it('mounts and loads account options for the GL tab', async () => {
    mount(Reports, { global: { stubs } })
    await flushPromises()
    expect(tree).toHaveBeenCalled()
  })

  it('runs the trial balance with the selected range', async () => {
    const wrapper = mount(Reports, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReportsVm
    await vm.runTrialBalance()
    await flushPromises()

    expect(trialBalance).toHaveBeenCalledTimes(1)
    expect(vm.tb).toBeTruthy()
  })
})
