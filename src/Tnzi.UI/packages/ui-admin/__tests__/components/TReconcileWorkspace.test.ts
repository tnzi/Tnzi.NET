import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TReconcileWorkspace from '../../src/components/finance/TReconcileWorkspace.vue'
import { BankTransactionStatus } from '../../src/services/bridges/finance-bridge'

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/', fullPath: '/', hash: '', name: 'x', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const txn = (id: string, over: Record<string, unknown> = {}) => ({
  id,
  accountId: 'a1',
  importBatchId: 'b1',
  txnDate: '2026-03-05',
  amount: -60,
  currency: 'USD',
  description: 'Fee',
  externalId: `e-${id}`,
  source: 'Csv',
  status: BankTransactionStatus.Pending,
  creationTime: '2026-03-05',
  ...over,
})

function makeBridge(items: unknown[]) {
  return {
    bankFeed: {
      transactions: vi.fn(async () => ({ items, totalCount: items.length, pageIndex: 1, pageSize: 20 })),
      candidates: vi.fn(async () => [
        { journalLineId: 'l1', journalEntryId: 'j1', entryNumber: 'JE-1', postingDate: '2026-03-05', memo: 'Fee', amount: -60 },
      ]),
      confirm: vi.fn(async () => txn('x1', { status: BankTransactionStatus.Matched })),
      createDocument: vi.fn(async () => ({ docType: 'Expense', docId: 'd1', posted: true, matched: true })),
      exclude: vi.fn(async () => txn('x1', { status: BankTransactionStatus.Excluded })),
      unmatch: vi.fn(),
      restore: vi.fn(),
    },
  }
}

const stubs = {
  Progress: { name: 'Progress', template: '<div class="n-progress-stub" />' },
  Alert: { name: 'Alert', template: '<div class="n-alert-stub"><slot /></div>' },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  RadioGroup: { name: 'RadioGroup', template: '<div><slot /></div>' },
  RadioButton: { name: 'RadioButton', template: '<label><slot /></label>' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  Tab: { name: 'Tab', template: '<div><slot /></div>' },
  Tag: { name: 'Tag', template: '<span><slot /></span>' },
  Select: { name: 'Select', template: '<select />' },
  DataTable: { name: 'DataTable', template: '<div class="n-data-table-stub" />' },
}

function mountWorkspace(items: unknown[], over: Record<string, unknown> = {}) {
  const bridge = makeBridge(items)
  const wrapper = mount(TReconcileWorkspace, {
    props: {
      bridge: bridge as never,
      accountId: 'a1',
      hasDraftReconciliation: true,
      expenseAccountOptions: [],
      fundsAccountOptions: [],
      customerOptions: [],
      vendorOptions: [],
      t: (k: string) => k,
      ...over,
    },
    global: { stubs },
  })
  return { wrapper, bridge }
}

interface WorkspaceVm {
  remaining: number
  progress: number
  needsDraft: boolean
  status: BankTransactionStatus
  onMatch: (t: { id: string }, lineId: string) => void
  onCreate: (t: { id: string }, p: Record<string, unknown>) => void
}

describe('TReconcileWorkspace', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('counts down the review queue as lines settle', async () => {
    const { wrapper, bridge } = mountWorkspace([txn('x1'), txn('x2'), txn('x3')])
    await flushPromises()

    const vm = wrapper.vm as unknown as WorkspaceVm
    expect(vm.remaining).toBe(3)
    expect(vm.progress).toBe(0)

    vm.onMatch({ id: 'x1' }, 'l1')
    await flushPromises()

    expect(bridge.bankFeed.confirm).toHaveBeenCalledWith('x1', { journalLineId: 'l1' })
    // Settled rows leave in place, so the operator is not scrolled away.
    expect(vm.remaining).toBe(2)
    expect(vm.progress).toBe(33)
  })

  it('creates, posts and matches in one call (postAndMatch)', async () => {
    const { wrapper, bridge } = mountWorkspace([txn('x1')])
    await flushPromises()

    const vm = wrapper.vm as unknown as WorkspaceVm
    vm.onCreate({ id: 'x1' }, { docType: 'Expense', counterAccountId: 'acc-9' })
    await flushPromises()

    // Without postAndMatch the operator would have to leave the workspace to
    // post the draft and come back to match it.
    expect(bridge.bankFeed.createDocument).toHaveBeenCalledWith('x1', {
      docType: 'Expense',
      counterAccountId: 'acc-9',
      partyId: null,
      postAndMatch: true,
    })
    expect(vm.remaining).toBe(0)
  })

  it('warns up front when the account has no open reconciliation', async () => {
    const { wrapper } = mountWorkspace([txn('x1')], { hasDraftReconciliation: false })
    await flushPromises()

    const vm = wrapper.vm as unknown as WorkspaceVm
    expect(vm.needsDraft).toBe(true)
    expect(wrapper.find('.n-alert-stub').exists()).toBe(true)
  })

  it('does not warn on the settled tabs, where nothing is confirmed', async () => {
    const { wrapper } = mountWorkspace([txn('x1')], { hasDraftReconciliation: false })
    await flushPromises()

    const vm = wrapper.vm as unknown as WorkspaceVm
    vm.status = BankTransactionStatus.Excluded
    await flushPromises()
    expect(vm.needsDraft).toBe(false)
  })

  it('refuses write actions the caller has gated off', async () => {
    const { wrapper, bridge } = mountWorkspace([txn('x1')], { canMatch: false })
    await flushPromises()

    const vm = wrapper.vm as unknown as WorkspaceVm
    vm.onMatch({ id: 'x1' }, 'l1')
    await flushPromises()
    expect(bridge.bankFeed.confirm).not.toHaveBeenCalled()
  })
})
