import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Chart of Accounts page - TCrudPage with a seed-default toolbar action and a
 * dynamic parent-account fieldRenderer fed from the account tree.
 * Mocks the finance-bridge boundary (page convention).
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/accounts', fullPath: '/admin/finance/accounts', hash: '', name: 'finance.accounts', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const accountsFetch = vi.fn(async () => ({
  items: [
    { id: 'a1', code: '1000', name: 'Assets', rootType: 'Asset', isGroup: true, isActive: true },
    { id: 'a2', code: '1200', name: 'Accounts Receivable', rootType: 'Asset', isGroup: false, systemRole: 'AccountsReceivable', isActive: true },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))
const accountsTree = vi.fn(async () => [
  { id: 'a1', code: '1000', name: 'Assets', rootType: 'Asset', isGroup: true, isActive: true, children: [] },
])
const seedDefault = vi.fn(async () => 26)

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    // account-config.ts 在模块加载期用这三个枚举建 label map，须真实 re-export。
    AccountRootType: original.AccountRootType,
    AccountSystemRole: original.AccountSystemRole,
    CashFlowActivity: original.CashFlowActivity,
    createFinanceBridge: () => ({
      accounts: {
        fetch: accountsFetch,
        tree: accountsTree,
        create: vi.fn(async (data: unknown) => ({ id: 'a3', ...(data as object) })),
        update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
        delete: vi.fn(async () => undefined),
        seedDefault,
      },
    }),
  }
})

import Accounts from '../../../src/pages/finance/Accounts.vue'

const stubs = {
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Popconfirm: { name: 'Popconfirm', emits: ['positive-click'], template: '<div><slot name="trigger" /><slot /></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Select: { name: 'Select', template: '<select class="n-select-stub" />' },
  Input: { name: 'Input', template: '<input />' },
}

interface AccountsVm {
  seedDefault: () => Promise<void>
}

describe('Finance Accounts page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    accountsFetch.mockClear()
    accountsTree.mockClear()
    seedDefault.mockClear()
  })

  it('mounts and renders the account tree (indented list + parent options)', async () => {
    mount(Accounts, { global: { stubs } })
    await flushPromises()
    // The chart of accounts renders from the tree endpoint (indented hierarchy), not the flat paged list.
    expect(accountsTree).toHaveBeenCalled()
  })

  it('seed-default action calls the bridge and refreshes', async () => {
    const wrapper = mount(Accounts, { global: { stubs } })
    await flushPromises()
    accountsTree.mockClear()

    await (wrapper.vm as unknown as AccountsVm).seedDefault()
    await flushPromises()

    expect(seedDefault).toHaveBeenCalledTimes(1)
    expect(accountsTree).toHaveBeenCalled()
  })
})
