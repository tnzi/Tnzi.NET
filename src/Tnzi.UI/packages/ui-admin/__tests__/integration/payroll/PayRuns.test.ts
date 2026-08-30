import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Payroll Pay Runs page - read-only list with lifecycle actions, payslip
 * detail drawer, and pay drawer.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/payroll/pay-runs', fullPath: '/admin/payroll/pay-runs', hash: '', name: 'payroll.payRuns', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchRuns = vi.fn(async () => ({
  items: [{ id: 'r1', number: 'PR-000001', status: 'Posted', periodStart: '2026-07-01', periodEnd: '2026-07-31', payDate: '2026-08-05', frequency: 'Monthly', source: 'Internal', employeeCount: 3, netTotal: 9000, creationTime: '2026-07-31' }],
  totalCount: 1, pageIndex: 1, pageSize: 20,
}))

const fetchInputs = vi.fn(async () => [
  { id: 'i1', payRunId: 'r1', employeeId: 'e1', employeeCode: 'E1', employeeName: 'One', componentId: 'c1', componentCode: 'BONUS', componentName: 'Bonus', componentType: 'Earning', amount: 5000, note: 'H1', creationTime: '2026-08-01' },
])
const emptyPage = { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }

vi.mock('../../../src/services/bridges/payroll-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    ...original,
    createPayrollBridge: () => ({
      runs: {
        fetch: fetchRuns,
        getById: vi.fn(async () => null),
        createDraft: vi.fn(), updateDraft: vi.fn(), deleteDraft: vi.fn(),
        calculate: vi.fn(), post: vi.fn(), pay: vi.fn(), voidRun: vi.fn(),
        payslips: vi.fn(async () => []), payslip: vi.fn(async () => null),
        updatePayslipInputs: vi.fn(), createFromExternal: vi.fn(),
        inputs: fetchInputs, setInput: vi.fn(), deleteInput: vi.fn(),
      },
      structures: { fetch: vi.fn(async () => emptyPage) },
      components: { fetch: vi.fn(async () => emptyPage) },
      employees: { fetch: vi.fn(async () => emptyPage) },
    }),
  }
})

import Page from '../../../src/pages/payroll/PayRuns.vue'

// 捕获 TCrudPage 的 row-actions,以便在测试里驱动"查看"这条路径。
let capturedRowActions: Array<{ key: string; onClick?: (row: unknown) => unknown }> = []

const stubs = {
  TCrudPage: {
    name: 'TCrudPage',
    props: ['state', 'allColumns', 'title', 'rowActions', 'translate', 'detailWidth', 'detailTitle'],
    template: '<div class="t-crud-page-stub" />',
    created(this: { rowActions: typeof capturedRowActions }) {
      capturedRowActions = this.rowActions ?? []
    },
  },
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Descriptions: { name: 'Descriptions', template: '<div><slot /></div>' },
  DescriptionsItem: { name: 'DescriptionsItem', template: '<div><slot /></div>' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  Select: { name: 'Select', template: '<select />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
}

describe('Payroll Pay Runs page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchRuns.mockClear()
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchRuns.mock.calls.length).toBeGreaterThan(0)
  })

  it('loads the run one-time inputs when a run is viewed', async () => {
    fetchInputs.mockClear()
    capturedRowActions = []
    mount(Page, { global: { stubs } })
    await flushPromises()

    const view = capturedRowActions.find((a) => a.key === 'view' || a.key === 'detail')
    expect(view).toBeTruthy()
    await view!.onClick?.({ id: 'r1' })
    await flushPromises()

    // 一次性输入与工资单一起进详情:批次视图是它的归属地(它挂在批次上,不挂在 payslip 上)。
    expect(fetchInputs).toHaveBeenCalledWith('r1')
  })
})
