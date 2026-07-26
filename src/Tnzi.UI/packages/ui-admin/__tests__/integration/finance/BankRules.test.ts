import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bank rules page.
 *
 * The two things worth locking: moving a rule submits the whole new order
 * (priority is positional, not a number people type), and the dry run tells the
 * operator when a higher-priority rule will take the lines anyway.
 */
const h = vi.hoisted(() => ({
  fetch: vi.fn(),
  reorder: vi.fn(),
  test: vi.fn(),
  getById: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {}, params: {}, path: '/admin/finance/banking/bank-rules',
    fullPath: '/admin/finance/banking/bank-rules', hash: '', name: 'finance.bankRules', meta: {},
  }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const rows = [
  { id: 'r1', name: 'Coffee', priority: 1, isEnabled: true, autoApply: false, matchMode: 'All', conditions: [{ id: 'c1', lineNumber: 1, field: 'Description', operator: 'Contains', value: 'starbucks' }] },
  { id: 'r2', name: 'Rent', priority: 2, isEnabled: true, autoApply: true, matchMode: 'All', conditions: [] },
]

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    ...original,
    createFinanceBridge: () => ({
      bankRules: {
        fetch: h.fetch,
        getById: h.getById,
        create: vi.fn(),
        update: vi.fn(),
        delete: vi.fn(),
        reorder: h.reorder,
        test: h.test,
      },
      accounts: { tree: vi.fn(async () => []), fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      customers: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      vendors: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      items: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      taxes: { codes: vi.fn(async () => []) },
    }),
  }
})

import Page from '../../../src/pages/finance/BankRules.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data', 'columns'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  Switch: { name: 'Switch', template: '<input type="checkbox" />' },
  Alert: { name: 'Alert', template: '<div><slot /></div>' },
  Divider: { name: 'Divider', template: '<hr />' },
}

describe('Finance bank rules page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.fetch.mockReset().mockResolvedValue({ items: rows, totalCount: 2, pageIndex: 1, pageSize: 20 })
    h.reorder.mockReset().mockResolvedValue(undefined)
    h.test.mockReset().mockResolvedValue({ evaluated: 10, matched: 2, rows: [] })
    h.getById.mockReset().mockResolvedValue(rows[0])
  })

  it('mounts and loads its rules', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(h.fetch).toHaveBeenCalled()
  })

  it('submits the whole new order when a rule moves', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const actions = wrapper.vm.rowActions as Array<{ key: string; onClick: (r: unknown) => void }>
    actions.find((a) => a.key === 'moveDown')!.onClick(rows[0])
    await flushPromises()

    // Positional, not a number someone types: two rules with the same priority
    // would leave nobody able to say which one wins.
    expect(h.reorder).toHaveBeenCalledWith(['r2', 'r1'])
  })

  it('hides the move action that would run off the end of the list', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const actions = wrapper.vm.rowActions as Array<{ key: string; show?: (r: unknown) => boolean }>
    const visible = (row: unknown) => actions.filter((a) => !a.show || a.show(row)).map((a) => a.key)

    expect(visible(rows[0])).not.toContain('moveUp')
    expect(visible(rows[1])).not.toContain('moveDown')
  })

  it('warns when a higher-priority rule takes the lines anyway', async () => {
    h.test.mockResolvedValue({
      evaluated: 10,
      matched: 2,
      rows: [
        { transactionId: 't1', txnDate: '2026-07-20', amount: -12, description: 'STARBUCKS', winningRuleId: 'r9', winningRuleName: 'Specific' },
        { transactionId: 't2', txnDate: '2026-07-21', amount: -8, description: 'STARBUCKS', winningRuleId: 'r1', winningRuleName: 'Coffee' },
      ],
    })

    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const actions = wrapper.vm.rowActions as Array<{ key: string; onClick: (r: unknown) => void }>
    actions.find((a) => a.key === 'test')!.onClick(rows[0])
    await flushPromises()

    expect(h.test).toHaveBeenCalledWith('r1', expect.anything())
    expect((wrapper.vm as unknown as { stolenCount: number }).stolenCount).toBe(1)
  })
})
