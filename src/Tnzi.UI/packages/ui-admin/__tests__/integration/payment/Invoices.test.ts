import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Invoices integration test.
 *
 * The page uses TListShell + TTableRenderer (useCrudPage fetch-only) for the
 * list chrome, while keeping custom NModal lifecycle dialogs (mark-paid /
 * cancel) and a send-invoice popconfirm as siblings. There is no create/edit
 * form (invoices aren't authored here).
 *
 * Strategy: mock the invoice bridge + admin client, mount with stubs, then
 * assert list rendering, lifecycle actions (send / mark-paid / cancel) call the
 * bridge and refresh, and the status filter drives a refetch with the status.
 */

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('naive-ui')
  return {
    ...actual,
    useMessage: () => ({
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
    }),
  }
})

const mockGetList = vi.fn(async () => ({
  items: [
    {
      id: 'inv-1',
      invoiceNo: 'INV-001',
      customerName: 'Acme Corp',
      customerEmail: 'billing@acme.com',
      amount: 100,
      paidAmount: 0,
      dueAmount: 100,
      currency: 'USD',
      status: 'Pending', // send / mark-paid / cancel all available
      invoiceDate: '2026-01-01',
      dueDate: '2026-02-01',
    },
  ],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

const mockMarkAsPaid = vi.fn(async () => undefined)
const mockCancel = vi.fn(async () => undefined)
const mockSend = vi.fn(async () => undefined)

vi.mock('../../../src/services/bridges/invoice-bridge', () => ({
  createInvoiceBridge: () => ({
    getList: mockGetList,
    markAsPaid: mockMarkAsPaid,
    cancel: mockCancel,
    send: mockSend,
  }),
}))

import Invoices from '../../../src/pages/payment/Invoices.vue'

const stubs = {
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="n-data-table-stub" :data-rows="(data || []).length"></div>',
  },
  Pagination: {
    name: 'Pagination',
    props: ['page', 'itemCount', 'pageSize'],
    emits: ['update:page', 'update:pageSize'],
    template: '<div class="n-pagination-stub"></div>',
  },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: {
    name: 'Button',
    props: ['loading', 'disabled'],
    template: '<button @click="$emit(\'click\')"><slot /><slot name="icon" /></button>',
  },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: {
    name: 'Popover',
    props: ['show'],
    template: '<div><slot name="trigger" /><slot /></div>',
  },
  Popconfirm: {
    name: 'Popconfirm',
    emits: ['positiveClick'],
    template: '<div class="n-popconfirm-stub"><slot name="trigger" /><button class="popconfirm-ok" @click="$emit(\'positiveClick\')">OK</button></div>',
  },
  Select: {
    name: 'Select',
    props: ['value', 'options'],
    emits: ['update:value'],
    template: '<select class="n-select-stub" :value="value" @change="$emit(\'update:value\', $event.target.value)"><option v-for="o in (options || [])" :key="o.value" :value="o.value">{{ o.label }}</option></select>',
  },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  Space: { name: 'Space', template: '<div class="n-space"><slot /></div>' },
  Tag: { name: 'Tag', template: '<span class="n-tag"><slot /></span>' },
  Alert: { name: 'Alert', template: '<div class="n-alert"><slot /></div>' },
}

interface InvoiceRow {
  id: string
  invoiceNo: string
  amount: number
  paidAmount: number
  dueAmount: number
  currency: string
  status: string
  invoiceDate: string
  dueDate: string
}

const sampleRow: InvoiceRow = {
  id: 'inv-1',
  invoiceNo: 'INV-001',
  amount: 100,
  paidAmount: 0,
  dueAmount: 100,
  currency: 'USD',
  status: 'Pending',
  invoiceDate: '2026-01-01',
  dueDate: '2026-02-01',
}

describe('Invoices page (TListShell + lifecycle modals)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockGetList.mockClear()
    mockMarkAsPaid.mockClear()
    mockCancel.mockClear()
    mockSend.mockClear()
  })

  it('mounts without throwing', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    expect(wrapper.exists()).toBe(true)
  })

  it('calls bridge.getList on mount', async () => {
    mount(Invoices, { global: { stubs } })
    await flushPromises()
    expect(mockGetList).toHaveBeenCalledTimes(1)
  })

  it('renders TListShell + table via TTableRenderer', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.t-list-shell').exists()).toBe(true)
    const table = wrapper.find('.n-data-table-stub')
    expect(table.exists()).toBe(true)
    expect(table.attributes('data-rows')).toBe('1')
  })

  it('lifecycle modals are closed initially', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.n-modal-stub').exists()).toBe(false)
  })

  it('mark-paid overlay opens and defaults paidAmount to the due amount', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      markPaidDetail: { open: (a: string, r: InvoiceRow) => Promise<void>; visible: { value: boolean } }
      markPaidForm: { paidAmount: number }
    }
    await vm.markPaidDetail.open('edit', sampleRow)
    await flushPromises()
    expect(vm.markPaidDetail.visible.value).toBe(true)
    expect(vm.markPaidForm.paidAmount).toBe(100)
  })

  it('confirmMarkPaid calls bridge.markAsPaid and refreshes', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      markPaidDetail: { open: (a: string, r: InvoiceRow) => Promise<void> }
      confirmMarkPaid: () => Promise<void>
    }
    await vm.markPaidDetail.open('edit', sampleRow)
    await flushPromises()
    await vm.confirmMarkPaid()
    await flushPromises()
    expect(mockMarkAsPaid).toHaveBeenCalledTimes(1)
    expect(mockMarkAsPaid.mock.calls[0][0]).toBe('inv-1')
    // getList: once on mount + once after mark-paid
    expect(mockGetList).toHaveBeenCalledTimes(2)
  })

  it('cancel overlay opens', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      cancelDetail: { open: (a: string, r: InvoiceRow) => Promise<void>; visible: { value: boolean } }
    }
    await vm.cancelDetail.open('edit', sampleRow)
    await flushPromises()
    expect(vm.cancelDetail.visible.value).toBe(true)
  })

  it('confirmCancel calls bridge.cancel with the reason and refreshes', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      cancelDetail: { open: (a: string, r: InvoiceRow) => Promise<void> }
      cancelReason: string
      confirmCancel: () => Promise<void>
    }
    await vm.cancelDetail.open('edit', sampleRow)
    await flushPromises()
    vm.cancelReason = 'duplicate'
    await vm.confirmCancel()
    await flushPromises()
    expect(mockCancel).toHaveBeenCalledTimes(1)
    expect(mockCancel.mock.calls[0][0]).toBe('inv-1')
    expect(mockCancel.mock.calls[0][1]).toBe('duplicate')
    expect(mockGetList).toHaveBeenCalledTimes(2)
  })

  it('sendInvoice calls bridge.send and refreshes', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { sendInvoice: (row: InvoiceRow) => Promise<void> }
    await vm.sendInvoice(sampleRow)
    await flushPromises()
    expect(mockSend).toHaveBeenCalledTimes(1)
    expect(mockSend.mock.calls[0][0]).toBe('inv-1')
    expect(mockGetList).toHaveBeenCalledTimes(2)
  })

  it('status filter change drives setFilters + refetch carrying the status', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      statusFilter: string | null
      onStatusChange: () => void
    }
    vm.statusFilter = 'Paid'
    vm.onStatusChange()
    await flushPromises()
    expect(mockGetList).toHaveBeenCalledTimes(2)
    const secondCall = mockGetList.mock.calls[1][0] as unknown as { status: string | null }
    expect(secondCall.status).toBe('Paid')
  })

  it('has no create button (read-only useCrudPage, no #primary slot)', async () => {
    const wrapper = mount(Invoices, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(false)
  })
})
