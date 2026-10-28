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

  it('unmatch and restore send a line back to review, so the count goes up', async () => {
    // Unmatch (Categorized) and Restore (Excluded) both move the line back to
    // Pending. Counting them down showed fewer lines left, and a finished bar,
    // while more lines were waiting.
    const pending = [txn('p1')]
    const matched = [txn('m1', { status: BankTransactionStatus.Matched }), txn('m2', { status: BankTransactionStatus.Matched })]
    const excluded = [txn('e1', { status: BankTransactionStatus.Excluded })]
    const { wrapper, bridge } = mountWorkspace(pending)
    bridge.bankFeed.transactions.mockImplementation(async (q: { filters: { status: BankTransactionStatus } }) => {
      const items = q.filters.status === BankTransactionStatus.Matched ? matched : q.filters.status === BankTransactionStatus.Excluded ? excluded : pending
      return { items, totalCount: items.length, pageIndex: 1, pageSize: 20 }
    })
    bridge.bankFeed.unmatch.mockResolvedValue(undefined)
    bridge.bankFeed.restore.mockResolvedValue(undefined)
    await flushPromises()

    type Vm = WorkspaceVm & { settledActions: Array<{ key: string; onClick: (r: unknown) => void }> }
    const vm = wrapper.vm as unknown as Vm
    expect(vm.remaining).toBe(1)

    vm.status = BankTransactionStatus.Matched
    await flushPromises()
    vm.settledActions.find((a) => a.key === 'unmatch')!.onClick(matched[0])
    await flushPromises()
    expect(bridge.bankFeed.unmatch).toHaveBeenCalledWith('m1')
    expect(vm.remaining).toBe(2)

    vm.status = BankTransactionStatus.Excluded
    await flushPromises()
    vm.settledActions.find((a) => a.key === 'restore')!.onClick(excluded[0])
    await flushPromises()
    expect(vm.remaining).toBe(3)
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

// The account selector sits on the same screen, so an operator can switch
// accounts (or tabs) while a page is still loading. Whatever answers last must
// not decide what is on screen: B's workspace listing A's lines means OK
// confirms or creates documents for A while the operator believes it is B.
describe('TReconcileWorkspace - a slower answer for a previous account or tab is dropped', () => {
  beforeEach(() => setActivePinia(createPinia()))

  function deferred<T>() {
    let resolve!: (v: T) => void
    const promise = new Promise<T>((r) => { resolve = r })
    return { promise, resolve }
  }
  const page = (items: unknown[]) => ({ items, totalCount: items.length, pageIndex: 1, pageSize: 20 })
  type Vm = WorkspaceVm & { rows: Array<{ id: string }> }

  it('keeps the newer account when the older one answers last', async () => {
    const { wrapper, bridge } = mountWorkspace([])
    const slowA = deferred<ReturnType<typeof page>>()
    bridge.bankFeed.transactions.mockReset()
    bridge.bankFeed.transactions
      .mockImplementationOnce(() => slowA.promise)
      .mockImplementationOnce(async () => page([txn('b1', { accountId: 'b' })]))

    await wrapper.setProps({ accountId: 'a' })
    await wrapper.setProps({ accountId: 'b' })
    await flushPromises()
    slowA.resolve(page([txn('a1', { accountId: 'a' }), txn('a2', { accountId: 'a' })]))
    await flushPromises()

    const vm = wrapper.vm as unknown as Vm
    expect(vm.rows.map((r) => r.id)).toEqual(['b1'])
    expect(vm.remaining).toBe(1)
  })

  it('keeps the newer tab when the older one answers last', async () => {
    const { wrapper, bridge } = mountWorkspace([txn('p1')])
    await flushPromises()
    const slowPending = deferred<ReturnType<typeof page>>()
    bridge.bankFeed.transactions.mockReset()
    bridge.bankFeed.transactions
      .mockImplementationOnce(() => slowPending.promise)
      .mockImplementationOnce(async () => page([txn('m1', { status: BankTransactionStatus.Matched })]))

    const vm = wrapper.vm as unknown as Vm
    ;(wrapper.vm as unknown as { reload: () => Promise<void> }).reload()
    vm.status = BankTransactionStatus.Matched
    await flushPromises()
    slowPending.resolve(page([txn('p1'), txn('p2')]))
    await flushPromises()

    expect(vm.rows.map((r) => r.id)).toEqual(['m1'])
  })

  it('does not count a line of the previous account against the new one', async () => {
    const { wrapper, bridge } = mountWorkspace([txn('x1')])
    await flushPromises()
    const slowConfirm = deferred<unknown>()
    bridge.bankFeed.confirm.mockImplementationOnce(() => slowConfirm.promise as never)
    bridge.bankFeed.transactions.mockImplementation(async () => page([txn('b1'), txn('b2'), txn('b3')]))

    const vm = wrapper.vm as unknown as Vm
    vm.onMatch({ id: 'x1' }, 'l1')
    await wrapper.setProps({ accountId: 'b' })
    await flushPromises()
    expect(vm.remaining).toBe(3)

    slowConfirm.resolve(txn('x1', { status: BankTransactionStatus.Matched }))
    await flushPromises()

    expect(vm.remaining).toBe(3)
    expect(vm.rows.map((r) => r.id)).toEqual(['b1', 'b2', 'b3'])
    expect(wrapper.emitted('settled')).toHaveLength(1)
  })
})
