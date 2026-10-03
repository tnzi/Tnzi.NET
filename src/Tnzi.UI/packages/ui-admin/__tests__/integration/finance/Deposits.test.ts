import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bank Deposits page - two tabs (undeposited receipts + deposits).
 *
 * The queue lists posted inbound receipts sitting on a clearing account that no
 * live deposit has claimed, and records a deposit from the selection; the
 * deposits tab exposes post / void / delete row actions conditional on status.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Mutable per test: a cold deep link is a mount with `?detail=view:<id>` already in the URL.
const routeState = vi.hoisted(() => ({ query: {} as Record<string, string> }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeState.query, params: {}, path: '/admin/finance/deposits', fullPath: '/admin/finance/deposits', hash: '', name: 'finance.deposits', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchDeposits = vi.fn(async () => ({
  items: [
    {
      id: 'd1', number: 'DEP-000001', status: 'Posted', fromAccountId: 'a1130', fromAccountName: '1130 Undeposited Funds',
      toAccountId: 'a1120', toAccountName: '1120 Bank Account', depositDate: '2026-03-25', currency: 'USD',
      exchangeRate: 1, amount: 5000, baseAmount: 5000, concurrencyStamp: 'x', creationTime: '2026-03-25', lines: [],
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const undeposited = vi.fn(async () => [
  { paymentEntryId: 'p1', paymentNumber: 'PMT-001', partyType: 'Customer', partyId: 'c1', partyName: 'Acme', docDate: '2026-03-20', currency: 'USD', amount: 1200, paymentMethod: 'Check', reference: '4471' },
  { paymentEntryId: 'p2', paymentNumber: 'PMT-002', partyType: 'Customer', partyId: 'c2', partyName: 'Globex', docDate: '2026-03-21', currency: 'USD', amount: 300, paymentMethod: 'Check', reference: '4472' },
  { paymentEntryId: 'p3', paymentNumber: 'PMT-003', partyType: 'Customer', partyId: 'c3', partyName: 'Initech', docDate: '2026-03-22', currency: 'CAD', amount: 800, paymentMethod: 'Check', reference: '4473' },
])

const depositSection = {
  fetch: fetchDeposits,
  // `d9` is not on the loaded page: only the by-id load can resolve it, and only
  // the full record carries the lines.
  getById: vi.fn(async (id: string) =>
    id === 'd9'
      ? {
          id: 'd9', number: 'DEP-000009', status: 'Posted', fromAccountName: '1130 Undeposited Funds', toAccountName: '1120 Bank Account',
          depositDate: '2026-03-28', currency: 'USD', amount: 700, lines: [{ id: 'dl1', paymentNumber: 'PMT-009', amount: 700 }],
        }
      : null,
  ),
  createDraft: vi.fn(),
  updateDraft: vi.fn(),
  deleteDraft: vi.fn(),
  post: vi.fn(),
  voidDoc: vi.fn(),
  undeposited,
}

// The page resolves its account pickers through createFinanceOptionSources,
// which reads the account tree off the same bridge.
// The page must find the undeposited-funds account by `systemRole`, not by
// matching English words in the display label - hence a deliberately
// non-English name here.
const accountTree = vi.fn(async () => [
  { id: 'a1120', code: '1120', name: 'Bank Account', isGroup: false, isActive: true, cashFlowActivity: 'CashEquivalent', children: [] },
  { id: 'a1130', code: '1130', name: '待存款项', isGroup: false, isActive: true, cashFlowActivity: 'CashEquivalent', systemRole: 'UndepositedFunds', children: [] },
  { id: 'a4100', code: '4100', name: 'Interest Income', isGroup: false, isActive: true, cashFlowActivity: 'Operating', children: [] },
])

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    FinanceDocumentStatus: original.FinanceDocumentStatus,
    CashFlowActivity: original.CashFlowActivity,
    AccountSystemRole: original.AccountSystemRole,
    createFinanceBridge: () => ({
      accounts: { tree: accountTree },
      deposits: depositSection,
    }),
  }
})

import Page from '../../../src/pages/finance/Deposits.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  TabPane: { name: 'TabPane', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  DatePicker: { name: 'DatePicker', template: '<input />' },
  Descriptions: { name: 'Descriptions', template: '<div><slot /></div>' },
  DescriptionsItem: { name: 'DescriptionsItem', template: '<div><slot /></div>' },
}

interface DepositVm {
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }>
  sourceAccountId: string | null
  destinationOptions: Array<{ label: string; value: string }>
  checkedQueueKeys: string[]
  checkedTotal: number
  queueCurrency: string | null
  queueCurrencies: string[]
  queueRows: Array<{ paymentEntryId: string; currency: string }>
  queueKpis: { count: number; total: number; currency?: string }
  otherFunds: Array<{ key: number; accountId: string | null; amount: number | null; description: string | null }>
  addFundsLine: () => void
  canSubmit: boolean
  incompleteFundsLines: unknown[]
  submitCreate: () => Promise<void>
  openCreate: () => void
  createForm: { toAccountId: string | null; depositDate: number | null; reference: string | null; memo: string | null }
}

describe('Finance Deposits page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchDeposits.mockClear()
    undeposited.mockClear()
    accountTree.mockClear()
    // 写路径的 mock 也必须清：它们是模块级共享的，不清的话
    // `toHaveBeenCalledWith` 会匹配到**上一条用例**的调用，断言于是空过。
    depositSection.createDraft.mockClear()
    depositSection.updateDraft.mockClear()
    depositSection.deleteDraft.mockClear()
    depositSection.post.mockClear()
    depositSection.voidDoc.mockClear()
    depositSection.getById.mockClear()
    routeState.query = {}
  })

  it('restores a shared ?detail=view:<id> for a deposit that is not on the loaded page', async () => {
    routeState.query = { detail: 'view:d9' }
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm & {
      crud: { formModal: { visible: { value: boolean }; mode: { value: string | null } } }
      viewed: { id: string; lines: unknown[] } | null
      activeSection: string
    }
    expect(depositSection.getById).toHaveBeenCalledWith('d9')
    expect(vm.crud.formModal.visible.value).toBe(true)
    expect(vm.crud.formModal.mode.value).toBe('view')
    expect(vm.viewed?.lines).toHaveLength(1)
    // The drawer lives in the Deposits tab; a link without `?section=` must land there.
    expect(vm.activeSection).toBe('deposits')
    expect(wrapper.text() + document.body.textContent).toContain('DEP-000009')
  })

  it('mounts, loads deposits, and defaults the source account to Undeposited Funds', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    expect(fetchDeposits.mock.calls.length).toBeGreaterThan(0)

    // Most deployments have exactly one undeposited-funds account; preselecting
    // it saves a click that would otherwise be mandatory before anything shows.
    // The account is found by `systemRole`: its display name here is Chinese, so
    // any rule that reads the label would find nothing.
    const vm = wrapper.vm as unknown as DepositVm
    expect(vm.sourceAccountId).toBe('a1130')
    expect(undeposited.mock.calls.length).toBeGreaterThan(0)
    expect(undeposited.mock.calls[0]![0]).toEqual({ accountId: 'a1130' })
  })

  it('excludes the source account from the destination list', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    const values = vm.destinationOptions.map((o) => o.value)
    expect(values).toContain('a1120')
    // Depositing an account into itself is refused by the backend; offering it
    // here would only let someone pick an option that is certain to fail.
    expect(values).not.toContain('a1130')
  })

  it('totals only the checked receipts', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    vm.queueCurrency = 'USD'
    await flushPromises()

    expect(vm.checkedTotal).toBe(0)
    vm.checkedQueueKeys = ['p1']
    await flushPromises()
    expect(vm.checkedTotal).toBe(1200)

    // Only what is on screen counts: switching currency clears the selection,
    // so the total can never mix two currencies.
    vm.checkedQueueKeys = ['p1', 'p2']
    await flushPromises()
    expect(vm.checkedTotal).toBe(1500)
  })

  it('gates post / void / delete on deposit status', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    const draft = { id: 'd1', status: 'Draft' }
    const posted = { id: 'd2', status: 'Posted' }
    const voided = { id: 'd3', status: 'Voided' }

    expect(byKey.post!.show!(draft)).toBe(true)
    expect(byKey.post!.show!(posted)).toBe(false)
    expect(byKey.void!.show!(posted)).toBe(true)
    expect(byKey.void!.show!(draft)).toBe(false)
    expect(byKey.void!.show!(voided)).toBe(false)
    expect(byKey.delete!.show!(draft)).toBe(true)
    // A posted deposit is immutable: it can only be voided, never deleted.
    expect(byKey.delete!.show!(posted)).toBe(false)
  })

  it('splits the queue by currency and never sums across currencies', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    // A clearing account that is not currency-restricted holds whatever came in.
    // One deposit is one currency, so the queue is split rather than summed:
    // adding USD to CAD produces a precise-looking number that is not money.
    expect(vm.queueCurrencies).toEqual(['CAD', 'USD'])
    expect(vm.queueCurrency).toBe('CAD')
    expect(vm.queueRows.map((r) => r.paymentEntryId)).toEqual(['p3'])
    expect(vm.queueKpis).toMatchObject({ count: 1, total: 800, currency: 'CAD' })

    vm.queueCurrency = 'USD'
    await flushPromises()
    expect(vm.queueRows.map((r) => r.paymentEntryId)).toEqual(['p1', 'p2'])
    expect(vm.queueKpis).toMatchObject({ count: 2, total: 1500, currency: 'USD' })
  })

  it('sends the selected currency so a foreign-currency deposit is possible', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    vm.queueCurrency = 'USD'
    vm.checkedQueueKeys = ['p1', 'p2']
    vm.createForm.toAccountId = 'a1120'
    await flushPromises()

    await vm.submitCreate()

    // Omitting `currency` makes the backend fall back to the base currency and
    // reject every receipt that is not in it - there would be no way to bank a
    // USD deposit from this page at all.
    expect(depositSection.createDraft).toHaveBeenCalledWith(
      expect.objectContaining({ currency: 'USD', paymentEntryIds: ['p1', 'p2'] }),
    )
  })

  it('carries other-funds lines and accepts a deposit made only of them', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    vm.createForm.toAccountId = 'a1120'
    await flushPromises()

    // No receipts checked yet: the backend rule is "at least one receipt OR one
    // other-funds line", so an interest-only deposit must be submittable.
    expect(vm.canSubmit).toBe(false)
    vm.addFundsLine()
    await flushPromises()
    // A blank line is not a line.
    expect(vm.canSubmit).toBe(false)

    vm.otherFunds[0]!.accountId = 'a4100'
    vm.otherFunds[0]!.amount = 40
    vm.otherFunds[0]!.description = 'Bank interest'
    await flushPromises()
    expect(vm.canSubmit).toBe(true)

    await vm.submitCreate()

    expect(depositSection.createDraft).toHaveBeenCalledWith(
      expect.objectContaining({
        paymentEntryIds: [],
        otherFunds: [{ accountId: 'a4100', amount: 40, description: 'Bank interest' }],
      }),
    )
  })

  it('refuses to silently drop a half-filled other-funds line', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    vm.queueCurrency = 'USD'
    vm.checkedQueueKeys = ['p1']
    vm.createForm.toAccountId = 'a1120'
    await flushPromises()
    expect(vm.canSubmit).toBe(true)

    // Account picked, amount left blank. Filtering this line away would post a
    // deposit that is short by that money, with nothing on screen saying so -
    // and the receipts alone are enough to keep the button live.
    vm.addFundsLine()
    vm.otherFunds[0]!.accountId = 'a4100'
    await flushPromises()
    expect(vm.canSubmit).toBe(false)

    vm.otherFunds[0]!.amount = 40
    await flushPromises()
    expect(vm.canSubmit).toBe(true)
  })

  it('ignores a line that was added and never touched', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    vm.queueCurrency = 'USD'
    vm.checkedQueueKeys = ['p1']
    vm.createForm.toAccountId = 'a1120'
    // Clicking "Add line" and changing your mind is not an error.
    vm.addFundsLine()
    await flushPromises()
    expect(vm.canSubmit).toBe(true)

    await vm.submitCreate()
    expect(depositSection.createDraft).toHaveBeenCalledWith(expect.objectContaining({ otherFunds: [] }))
  })

  it('drops checked receipts that a reload removed from the queue', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as DepositVm
    vm.queueCurrency = 'USD'
    vm.checkedQueueKeys = ['p1', 'p2']
    await flushPromises()
    expect(vm.checkedTotal).toBe(1500)

    // Someone else banked PMT-001 in the meantime.
    undeposited.mockResolvedValueOnce([
      { paymentEntryId: 'p2', paymentNumber: 'PMT-002', partyType: 'Customer', partyId: 'c2', partyName: 'Globex', docDate: '2026-03-21', currency: 'USD', amount: 300, paymentMethod: 'Check', reference: '4472' },
    ])
    await (vm as unknown as { loadQueue: () => Promise<void> }).loadQueue()
    await flushPromises()

    // Keeping p1 checked would submit a receipt that is no longer on screen,
    // and would make the on-screen total disagree with what gets sent.
    expect(vm.checkedQueueKeys).toEqual(['p2'])
    expect(vm.checkedTotal).toBe(300)
  })
})
