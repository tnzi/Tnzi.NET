import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bank Feed workspace — read-only transaction list driven by an account
 * picker + status filter, with match / exclude / create-document actions.
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

const confirm = vi.fn(async () => ({ id: 'x1', status: 'Matched' }))

const bankFeedSection = {
  transactions: fetchTxns,
  import: vi.fn(async () => ({ batchId: 'bt1', importedCount: 2, skippedCount: 0 })),
  pull: vi.fn(async () => ({ batchId: 'bt2', importedCount: 0, skippedCount: 0 })),
  suggest: vi.fn(async () => ({ evaluated: 2, suggested: 1, autoConfirmed: 0 })),
  candidates: vi.fn(async () => []),
  confirm,
  unmatch: vi.fn(),
  exclude: vi.fn(),
  restore: vi.fn(),
  createDocument: vi.fn(async () => ({ docType: 'Expense', docId: 'd1' })),
  batches: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })),
  deleteBatch: vi.fn(),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    BankTransactionStatus: original.BankTransactionStatus,
    BankTransactionSource: original.BankTransactionSource,
    BankFeedDocType: original.BankFeedDocType,
    CashFlowActivity: original.CashFlowActivity,
    PAYMENT_METHODS: original.PAYMENT_METHODS,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      customers: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 200 })) },
      vendors: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 200 })) },
      reconciliations: { create: vi.fn() },
      bankFeed: bankFeedSection,
    }),
  }
})

import Page from '../../../src/pages/finance/BankFeed.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
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
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }>
}

describe('Finance BankFeed page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchTxns.mockClear()
  })

  it('waits for an account before fetching, then loads transactions', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    // autoLoad is false — no account selected yet.
    expect(fetchTxns.mock.calls.length).toBe(0)

    const vm = wrapper.vm as unknown as BankFeedVm
    vm.accountId = 'a1'
    await flushPromises()
    expect(fetchTxns.mock.calls.length).toBeGreaterThan(0)
  })

  it('gates confirm on a pending row carrying a suggestion', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as BankFeedVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    const suggested = { id: 'x1', status: 'Pending', suggestedJournalLineId: 'l1' }
    const plain = { id: 'x2', status: 'Pending' }
    const matched = { id: 'x3', status: 'Matched' }

    expect(byKey.confirm!.show!(suggested)).toBe(true)
    expect(byKey.confirm!.show!(plain)).toBe(false)
    expect(byKey.exclude!.show!(plain)).toBe(true)
    expect(byKey.unmatch!.show!(matched)).toBe(true)
    expect(byKey.unmatch!.show!(suggested)).toBe(false)
  })
})
