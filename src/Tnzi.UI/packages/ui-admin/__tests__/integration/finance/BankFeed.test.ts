import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bank Feed page = statement ingestion + the reconcile workspace.
 *
 * The page owns the account picker, the import/pull/suggest actions and the
 * draft-reconciliation gate; the per-line review flow lives in
 * `TReconcileWorkspace` and has its own test.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/bank-feed', fullPath: '/admin/finance/bank-feed', hash: '', name: 'finance.bankFeed', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchTxns = vi.fn(async () => ({
  items: [
    { id: 'x1', accountId: 'a1', txnDate: '2026-03-05', amount: 250, currency: 'USD', description: 'Deposit', status: 'Pending', externalId: 'e1', source: 'Csv', suggestedJournalLineId: 'l1', matchRule: 'amount-date', matchConfidence: 0.8, creationTime: '2026-03-05' },
    { id: 'x2', accountId: 'a1', txnDate: '2026-03-06', amount: -60, currency: 'USD', description: 'Fee', status: 'Pending', externalId: 'e2', source: 'Csv', creationTime: '2026-03-06' },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

const fetchReconciliations = vi.fn(async () => ({ items: [] as unknown[], totalCount: 0, pageIndex: 1, pageSize: 1 }))
const createReconciliation = vi.fn(async () => ({ id: 'r1' }))

const bankFeedSection = {
  transactions: fetchTxns,
  import: vi.fn(async () => ({ batchId: 'bt1', importedCount: 2, skippedCount: 0 })),
  pull: vi.fn(async () => ({ batchId: 'bt2', importedCount: 0, skippedCount: 0 })),
  suggest: vi.fn(async () => ({ evaluated: 2, suggested: 1, autoConfirmed: 0 })),
  candidates: vi.fn(async () => []),
  confirm: vi.fn(async () => ({ id: 'x1', status: 'Matched' })),
  unmatch: vi.fn(),
  exclude: vi.fn(),
  restore: vi.fn(),
  createDocument: vi.fn(async () => ({ docType: 'Expense', docId: 'd1', posted: true, matched: true })),
  batches: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })),
  deleteBatch: vi.fn(),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    BankTransactionStatus: original.BankTransactionStatus,
    BankTransactionSource: original.BankTransactionSource,
    BankFeedDocType: original.BankFeedDocType,
    ReconciliationStatus: original.ReconciliationStatus,
    CashFlowActivity: original.CashFlowActivity,
    PAYMENT_METHODS: original.PAYMENT_METHODS,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      customers: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 200 })) },
      vendors: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 200 })) },
      reconciliations: { fetch: fetchReconciliations, create: createReconciliation },
      bankFeed: bankFeedSection,
    }),
  }
})

import Page from '../../../src/pages/finance/BankFeed.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Progress: { name: 'Progress', template: '<div />' },
  Alert: { name: 'Alert', template: '<div class="n-alert-stub"><slot /></div>' },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  Tab: { name: 'Tab', template: '<div><slot /></div>' },
  Tag: { name: 'Tag', template: '<span><slot /></span>' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  RadioGroup: { name: 'RadioGroup', template: '<div><slot /></div>' },
  RadioButton: { name: 'RadioButton', template: '<label><slot /></label>' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
}

interface BankFeedVm {
  accountId: string | null
  hasDraft: boolean
  createDraftReconciliation: () => Promise<void>
}

describe('Finance BankFeed page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchTxns.mockClear()
    fetchReconciliations.mockClear()
    createReconciliation.mockClear()
  })

  it('waits for an account before fetching, then loads transactions', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    // No account selected yet - the workspace is not even mounted.
    expect(fetchTxns.mock.calls.length).toBe(0)

    const vm = wrapper.vm as unknown as BankFeedVm
    vm.accountId = 'a1'
    await flushPromises()
    expect(fetchTxns.mock.calls.length).toBeGreaterThan(0)
  })

  it('probes for an open reconciliation and reports when there is none', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as BankFeedVm
    vm.accountId = 'a1'
    await flushPromises()

    expect(fetchReconciliations).toHaveBeenCalled()
    // The mocked probe returns an empty page, so the banner precondition holds.
    expect(vm.hasDraft).toBe(false)
  })

  it('starts a draft reconciliation from the workspace banner', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as BankFeedVm
    vm.accountId = 'a1'
    await flushPromises()

    await vm.createDraftReconciliation()
    expect(createReconciliation).toHaveBeenCalledWith(
      expect.objectContaining({ accountId: 'a1', statementEndingBalance: 0 }),
    )
    expect(vm.hasDraft).toBe(true)
  })

  it('fails OPEN when the reconciliation probe errors', async () => {
    fetchReconciliations.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as BankFeedVm
    vm.accountId = 'a1'
    await flushPromises()

    // A failed probe must not tell the operator to create a reconciliation
    // that may already exist; the backend 400 is the real gate.
    expect(vm.hasDraft).toBe(true)
  })
})
