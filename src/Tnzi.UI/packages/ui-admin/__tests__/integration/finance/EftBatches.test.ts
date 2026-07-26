import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * EFT Batches page - two tabs (payable queue + batches). The queue selects
 * bank-transfer payments and creates a batch; batches expose generate /
 * download / void row actions conditional on the batch status.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/eft-batches', fullPath: '/admin/finance/eft-batches', hash: '', name: 'finance.eftBatches', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchBatches = vi.fn(async () => ({
  items: [
    { id: 'eb1', number: 'EFT-0001', status: 'Draft', bankAccountId: 'ba1', bankAccountName: 'Operating', format: 'Nacha', currency: 'USD', effectiveDate: '2026-03-10', totalCount: 2, totalAmount: 500, concurrencyStamp: 'x', creationTime: '2026-03-10', lines: [] },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const queue = vi.fn(async () => [
  { paymentEntryId: 'p1', paymentNumber: 'PMT-001', partyType: 'Vendor', partyId: 'v1', payeeName: 'Acme', docDate: '2026-03-05', currency: 'USD', amount: 250, partyBankAccountId: 'pba1', partyBankAccountMasked: '6789', partyScheme: 'UsAba' },
])

const eftSection = {
  queue,
  fetch: fetchBatches,
  getById: vi.fn(async () => null),
  create: vi.fn(),
  generate: vi.fn(),
  voidBatch: vi.fn(),
  download: vi.fn(async () => new Blob()),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    EftBatchStatus: original.EftBatchStatus,
    EftFileFormat: original.EftFileFormat,
    createFinanceBridge: () => ({
      bankAccounts: { fetch: vi.fn(async () => ({ items: [{ id: 'ba1', name: 'Operating' }], totalCount: 1, pageIndex: 1, pageSize: 100 })) },
      eftBatches: eftSection,
    }),
  }
})

import Page from '../../../src/pages/finance/EftBatches.vue'

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

interface EftVm {
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }>
}

describe('Finance EftBatches page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchBatches.mockClear()
    queue.mockClear()
  })

  it('mounts and loads batches + queue', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchBatches.mock.calls.length).toBeGreaterThan(0)
    expect(queue.mock.calls.length).toBeGreaterThan(0)
  })

  it('gates generate / download / void on batch status', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as EftVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    const draft = { id: 'eb1', status: 'Draft' }
    const generated = { id: 'eb2', status: 'Generated' }
    const voided = { id: 'eb3', status: 'Voided' }

    expect(byKey.generate!.show!(draft)).toBe(true)
    expect(byKey.generate!.show!(generated)).toBe(false)
    expect(byKey.download!.show!(generated)).toBe(true)
    expect(byKey.download!.show!(draft)).toBe(false)
    expect(byKey.void!.show!(draft)).toBe(true)
    expect(byKey.void!.show!(voided)).toBe(false)
  })
})
