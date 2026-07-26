import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Financial Reports page - TTabsPage with seven report tabs (trial balance,
 * balance sheet, P&L, general ledger, AR/AP aging, tax summary) that aggregate
 * on demand, plus per-tab server-side CSV export.
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
const taxSummary = vi.fn(async () => ({
  from: '2026-01-01',
  to: '2026-12-31',
  baseCurrency: 'USD',
  rows: [
    { taxRateId: 'r1', rateName: 'GST 5', rate: 5, agencyId: 'ag1', agencyName: 'CRA', outputTax: 10, inputTax: 5, netTax: 5 },
  ],
  totalOutputTax: 10,
  totalInputTax: 5,
  totalNetTax: 5,
}))
const exportTrialBalanceCsv = vi.fn(async () => new Blob(['Code,Name'], { type: 'text/csv' }))
const cashFlow = vi.fn(async () => ({
  from: '2026-01-01',
  to: '2026-12-31',
  baseCurrency: 'USD',
  netProfit: 1000,
  operating: [{ accountId: 'a1', code: '1200', name: 'AR', rootType: 'Asset', subType: null, balance: -400 }],
  investing: [],
  financing: [],
  unclassified: [],
  totalOperating: 600,
  totalInvesting: 0,
  totalFinancing: 0,
  totalUnclassified: 0,
  netCashFlow: 600,
  openingCash: 0,
  closingCash: 600,
  cashMovement: 600,
  checkDifference: 0,
}))
const tree = vi.fn(async () => [])
const verify = vi.fn(async () => ({
  isConsistent: false,
  checkedBuckets: 12,
  totalDifferences: 2,
  differences: [
    { accountId: 'a1', period: 202601, currency: 'USD', kind: 'Mismatch', expectedDebit: 100, expectedCredit: 0, storedDebit: 90, storedCredit: 0 },
  ],
}))
const rebuild = vi.fn(async () => ({ buckets: 12, lines: 340, durationMs: 42 }))

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../src/services/bridges/finance-bridge')>()
  return {
    BalanceSummaryDifferenceKind: original.BalanceSummaryDifferenceKind,
    createFinanceBridge: () => ({
      accounts: { tree },
      reports: {
        trialBalance,
        balanceSheet: vi.fn(),
        profitAndLoss: vi.fn(),
        generalLedger: vi.fn(),
        arAging: vi.fn(),
        apAging: vi.fn(),
        taxSummary,
        cashFlow,
        exportTrialBalanceCsv,
        exportCashFlowCsv: vi.fn(),
        exportBalanceSheetCsv: vi.fn(),
        exportProfitAndLossCsv: vi.fn(),
        exportGeneralLedgerCsv: vi.fn(),
        exportArAgingCsv: vi.fn(),
        exportApAgingCsv: vi.fn(),
        exportTaxSummaryCsv: vi.fn(),
      },
      balanceSummary: { verify, rebuild },
    }),
  }
})

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
  runTaxSummary: () => Promise<void>
  runCashFlow: () => Promise<void>
  exportCsv: (active: string) => Promise<void>
  canExport: (active: string) => boolean
  runVerify: () => Promise<void>
  onMaintenanceSelect: (key: string) => void
  showMaintenance: boolean
  maintenanceOptions: Array<{ label: string; key: string }>
  verifyShow: boolean
  verifyResult: unknown
  tb: unknown
  tax: unknown
  cf: { checkDifference: number } | null
}

describe('Finance Reports page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    trialBalance.mockClear()
    taxSummary.mockClear()
    cashFlow.mockClear()
    exportTrialBalanceCsv.mockClear()
    tree.mockClear()
    verify.mockClear()
    rebuild.mockClear()
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

  it('runs the cash flow statement and exposes a zero identity check', async () => {
    const wrapper = mount(Reports, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReportsVm
    await vm.runCashFlow()
    await flushPromises()

    expect(cashFlow).toHaveBeenCalledTimes(1)
    expect(vm.cf).toBeTruthy()
    expect(vm.cf!.checkDifference).toBe(0)
    expect(vm.canExport('cash-flow')).toBe(true)
  })

  it('runs the tax summary for the selected range', async () => {
    const wrapper = mount(Reports, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReportsVm
    await vm.runTaxSummary()
    await flushPromises()

    expect(taxSummary).toHaveBeenCalledTimes(1)
    expect(vm.tax).toBeTruthy()
  })

  it('exports the active tab as CSV and gates GL export on account selection', async () => {
    const wrapper = mount(Reports, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReportsVm
    // GL export requires an account; other tabs are always exportable
    expect(vm.canExport('general-ledger')).toBe(false)
    expect(vm.canExport('trial-balance')).toBe(true)

    const objectUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock')
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined)
    try {
      vi.useFakeTimers()
      try {
        await vm.exportCsv('trial-balance')
        expect(exportTrialBalanceCsv).toHaveBeenCalledTimes(1)
        expect(objectUrl).toHaveBeenCalledTimes(1)
        // Released on a timer, not in the click's tick: a same-tick revoke can
        // cancel the download in Firefox / Safari.
        expect(revoke).not.toHaveBeenCalled()
        vi.runAllTimers()
        expect(revoke).toHaveBeenCalledWith('blob:mock')
      } finally {
        vi.useRealTimers()
      }
    } finally {
      objectUrl.mockRestore()
      revoke.mockRestore()
    }
  })

  it('shows the balance-summary maintenance dropdown (fail-open with no user loaded)', async () => {
    const wrapper = mount(Reports, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReportsVm
    // usePermissionGuard fail-opens when userInfo === null (no user in a bare mount).
    expect(vm.showMaintenance).toBe(true)
    expect(vm.maintenanceOptions).toHaveLength(2)
  })

  it('verifies the balance summary and opens the result modal', async () => {
    const wrapper = mount(Reports, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReportsVm
    await vm.runVerify()
    await flushPromises()

    expect(verify).toHaveBeenCalledTimes(1)
    expect(vm.verifyShow).toBe(true)
    expect(vm.verifyResult).toBeTruthy()
  })

  it('rebuilds the balance summary (confirm falls through without a dialog provider)', async () => {
    const wrapper = mount(Reports, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReportsVm
    vm.onMaintenanceSelect('rebuild')
    await flushPromises()

    expect(rebuild).toHaveBeenCalledTimes(1)
  })
})
