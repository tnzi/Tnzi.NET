import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Checks page — two tabs (print queue + register). The register is a read-only
 * CRUD list with conditional void / reprint row actions; the queue selects a
 * bank account, lists payable checks, and prints selected rows to a PDF.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/checks', fullPath: '/admin/finance/checks', hash: '', name: 'finance.checks', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchRegister = vi.fn(async () => ({
  items: [
    { id: 'ck1', bankAccountId: 'ba1', checkNumber: 1001, status: 'Issued', paymentEntryId: 'p1', payeeName: 'Acme', amount: 250, currency: 'USD', issueDate: '2026-03-05', isManual: false, concurrencyStamp: 'x', creationTime: '2026-03-05' },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const queue = vi.fn(async () => [
  { paymentEntryId: 'p1', paymentNumber: 'PMT-001', bankAccountId: 'ba1', bankAccountName: 'Operating', payeeName: 'Acme', docDate: '2026-03-05', currency: 'USD', amount: 250 },
])
const print = vi.fn(async () => new Blob(['%PDF'], { type: 'application/pdf' }))

const checksSection = {
  queue,
  fetch: fetchRegister,
  print,
  register: vi.fn(),
  reprint: vi.fn(async () => new Blob()),
  voidCheck: vi.fn(),
  spoil: vi.fn(),
  calibration: vi.fn(async () => new Blob()),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    CheckStatus: original.CheckStatus,
    createFinanceBridge: () => ({
      bankAccounts: { fetch: vi.fn(async () => ({ items: [{ id: 'ba1', name: 'Operating' }], totalCount: 1, pageIndex: 1, pageSize: 100 })) },
      checks: checksSection,
    }),
  }
})

import Page from '../../../src/pages/finance/Checks.vue'

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
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  DatePicker: { name: 'DatePicker', template: '<input />' },
}

interface ChecksVm {
  queueAccountId: string | null
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }>
}

describe('Finance Checks page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchRegister.mockClear()
    queue.mockClear()
  })

  it('mounts and loads the register list', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchRegister.mock.calls.length).toBeGreaterThan(0)
  })

  it('loads the print queue after a bank account is selected', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    expect(queue.mock.calls.length).toBe(0)

    const vm = wrapper.vm as unknown as ChecksVm
    vm.queueAccountId = 'ba1'
    await flushPromises()
    expect(queue).toHaveBeenCalledWith('ba1')
  })

  it('gates void / reprint on issued checks', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ChecksVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    const issued = { id: 'ck1', status: 'Issued', paymentEntryId: 'p1' }
    const manualIssued = { id: 'ck2', status: 'Issued' }
    const voided = { id: 'ck3', status: 'Void' }

    expect(byKey.void!.show!(issued)).toBe(true)
    expect(byKey.void!.show!(voided)).toBe(false)
    expect(byKey.reprint!.show!(issued)).toBe(true)
    expect(byKey.reprint!.show!(manualIssued)).toBe(false)
  })
})
