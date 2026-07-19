import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Bank Accounts page — TCrudPage over the bank account profile CRUD, plus a
 * set-next-check-number modal opened from a row action.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/bank-accounts', fullPath: '/admin/finance/bank-accounts', hash: '', name: 'finance.bankAccounts', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchList = vi.fn(async () => ({
  items: [
    { id: 'b1', accountId: 'a1', accountName: '1120 Bank', name: 'Operating', bankName: 'Acme Bank', scheme: 'UsAba', routingNumber: '021000021', accountNumberMasked: '6789', currency: 'USD', nextCheckNumber: 1001, checkStockType: 'PrePrinted', checkLayout: 'Voucher', offsetXMm: 0, offsetYMm: 0, eftFileCreationNumber: 1, concurrencyStamp: 'x', creationTime: '2026-03-01' },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const setNextCheckNumber = vi.fn(async () => ({ id: 'b1', nextCheckNumber: 2000 }))

const bankAccountSection = {
  fetch: fetchList,
  getById: vi.fn(async () => ({ id: 'b1', accountId: 'a1', name: 'Operating', scheme: 'UsAba', nextCheckNumber: 1001, checkStockType: 'PrePrinted', checkLayout: 'Voucher', offsetXMm: 0, offsetYMm: 0 })),
  create: vi.fn(),
  update: vi.fn(),
  setNextCheckNumber,
  delete: vi.fn(),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    BankNumberScheme: original.BankNumberScheme,
    CheckStockType: original.CheckStockType,
    CheckLayout: original.CheckLayout,
    CashFlowActivity: original.CashFlowActivity,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      bankAccounts: bankAccountSection,
    }),
  }
})

import Page from '../../../src/pages/finance/BankAccounts.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
}

interface BankAccountsVm {
  checkDetail: { open: (action: 'view', id: string) => void; data: { value: unknown } }
  nextCheckValue: number | null
  submitNextCheck: () => Promise<void>
}

describe('Finance BankAccounts page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    setNextCheckNumber.mockClear()
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })

  it('sets the next check number through the modal', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as BankAccountsVm
    vm.checkDetail.open('view', 'b1')
    await flushPromises()

    vm.nextCheckValue = 2000
    await vm.submitNextCheck()
    expect(setNextCheckNumber).toHaveBeenCalledWith('b1', 2000)
  })
})
