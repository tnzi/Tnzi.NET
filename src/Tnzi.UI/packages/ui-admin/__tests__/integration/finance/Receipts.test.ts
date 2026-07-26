import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Receipts page - upload → register → extract → convert. A read-only CRUD list
 * with a detail drawer that edits the extracted fields and converts the receipt
 * into an expense / bill draft; delete is hidden once converted.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), resolveUrl: (u: string) => u }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/receipts', fullPath: '/admin/finance/receipts', hash: '', name: 'finance.receipts', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const upload = vi.fn(async () => ({ id: 'file1', fileName: 'r.jpg', originalName: 'r.jpg', url: '/files/file1', size: 10, contentType: 'image/jpeg' }))

vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    files: {
      upload,
      previewUrl: (id: string) => `/api/files/${id}/preview`,
      downloadUrl: (id: string) => `/api/files/${id}/download`,
    },
  }),
}))

const fetchList = vi.fn(async () => ({
  items: [
    { id: 'rc1', fileId: 'file1', originalFileName: 'r.jpg', status: 'Extracted', vendorName: 'Acme', total: 99, currency: 'USD', confidence: 0.9, concurrencyStamp: 'x', creationTime: '2026-03-05' },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const createReceipt = vi.fn()
const convert = vi.fn(async () => ({ docType: 'Expense', docId: 'exp1' }))

const receiptsSection = {
  fetch: fetchList,
  getById: vi.fn(async () => ({ id: 'rc1', fileId: 'file1', status: 'Extracted', vendorName: 'Acme', matchedVendorId: 'v1' })),
  create: createReceipt,
  extract: vi.fn(async () => ({ id: 'rc1', status: 'Extracted' })),
  update: vi.fn(async () => ({ id: 'rc1', status: 'Extracted' })),
  convert,
  delete: vi.fn(),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    ReceiptStatus: original.ReceiptStatus,
    ReceiptDocType: original.ReceiptDocType,
    CashFlowActivity: original.CashFlowActivity,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      vendors: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 200 })) },
      receipts: receiptsSection,
    }),
  }
})

import Page from '../../../src/pages/finance/Receipts.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  RadioGroup: { name: 'RadioGroup', template: '<div><slot /></div>' },
  RadioButton: { name: 'RadioButton', template: '<label><slot /></label>' },
  Image: { name: 'Image', template: '<img />' },
  Alert: { name: 'Alert', template: '<div><slot /></div>' },
}

interface ReceiptsVm {
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }>
  convertForm: { docType: string; vendorId: string | null; accountId: string | null; paidFromAccountId: string | null }
  detail: { data: { value: unknown } }
  openConvert: () => void
  submitConvert: () => Promise<void>
}

describe('Finance Receipts page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    convert.mockClear()
  })

  it('mounts and loads the receipt list', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })

  it('hides delete on a converted receipt', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReceiptsVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    expect(byKey.delete!.show!({ id: 'rc1', status: 'Extracted' })).toBe(true)
    expect(byKey.delete!.show!({ id: 'rc2', status: 'Converted' })).toBe(false)
  })

  it('converts a receipt into a draft document', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as ReceiptsVm
    vm.detail.data.value = { id: 'rc1', status: 'Extracted', matchedVendorId: 'v1' }
    vm.openConvert()
    vm.convertForm.accountId = 'acc1'
    vm.convertForm.paidFromAccountId = 'cash1'
    await vm.submitConvert()
    expect(convert).toHaveBeenCalledWith('rc1', expect.objectContaining({ docType: 'Expense', vendorId: 'v1', accountId: 'acc1', paidFromAccountId: 'cash1' }))
  })
})
