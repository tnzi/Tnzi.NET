import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * EFT Batches page - two tabs (payable queue + batches). The queue selects
 * bank-transfer payments and creates a batch; batches expose generate /
 * download / void row actions conditional on the batch status.
 */
vi.mock('@tnzi/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/core')>()),
  downloadBlob: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Mutable per test: a cold deep link is a mount with `?detail=view:<id>` already in the URL.
const routeState = vi.hoisted(() => ({ query: {} as Record<string, string> }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeState.query, params: {}, path: '/admin/finance/eft-batches', fullPath: '/admin/finance/eft-batches', hash: '', name: 'finance.eftBatches', meta: {} }),
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
  { paymentEntryId: 'p1', paymentNumber: 'PMT-001', fundingAccountId: 'gl-operating', partyType: 'Vendor', partyId: 'v1', payeeName: 'Acme', docDate: '2026-03-05', currency: 'USD', amount: 250, partyBankAccountId: 'pba1', partyBankAccountMasked: '6789', partyScheme: 'UsAba' },
  { paymentEntryId: 'p2', paymentNumber: 'PMT-002', fundingAccountId: 'gl-payroll', partyType: 'Vendor', partyId: 'v2', payeeName: 'Initech', docDate: '2026-03-06', currency: 'USD', amount: 75, partyBankAccountId: 'pba2', partyBankAccountMasked: '1234', partyScheme: 'UsAba' },
])

const eftSection = {
  queue,
  fetch: fetchBatches,
  // `eb9` is not on the loaded page: only the by-id load can resolve it. The
  // list rows carry no lines; the full record does.
  getById: vi.fn(async (id: string) =>
    id === 'eb9'
      ? { id: 'eb9', number: 'EFT-0009', status: 'Generated', bankAccountName: 'Operating', format: 'Nacha', currency: 'USD', effectiveDate: '2026-03-12', totalCount: 1, totalAmount: 75, lines: [{ id: 'l1', payeeName: 'Initech', amount: 75 }] }
      : id === 'eb1'
        ? { id: 'eb1', number: 'EFT-0001', status: 'Draft', bankAccountName: 'Operating', format: 'Nacha', currency: 'USD', effectiveDate: '2026-03-10', totalCount: 2, totalAmount: 500, lines: [] }
        : null,
  ),
  create: vi.fn(),
  generate: vi.fn(),
  voidBatch: vi.fn(async (id: string) => ({ id, status: 'Voided' })),
  download: vi.fn(async () => new Blob()),
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    EftBatchStatus: original.EftBatchStatus,
    EftFileFormat: original.EftFileFormat,
    createFinanceBridge: () => ({
      bankAccounts: {
        fetch: vi.fn(async () => ({
          items: [
            { id: 'ba1', name: 'Operating', accountId: 'gl-operating' },
            { id: 'ba2', name: 'Payroll', accountId: 'gl-payroll' },
          ],
          totalCount: 2,
          pageIndex: 1,
          pageSize: 100,
        })),
      },
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
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean; onClick: (row: Record<string, unknown>) => void }>
  bankAccountOptions: Array<{ label: string; value: string }>
  onCheckedChange: (keys: string[]) => void
  openCreate: () => void
  createForm: { bankAccountId: string | null }
  crud: { formModal: { visible: { value: boolean }; mode: { value: string | null } } }
  viewed: { id: string; number: string; lines: unknown[] } | null
  activeSection: string
}

describe('Finance EftBatches page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchBatches.mockClear()
    queue.mockClear()
    eftSection.getById.mockClear()
    routeState.query = {}
  })

  it('opens a batch in the list engine\'s own view drawer, loading the full record', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as EftVm
    vm.rowActions.find((a) => a.key === 'detail')!.onClick({ id: 'eb1', number: 'EFT-0001', status: 'Draft' })
    await flushPromises()

    expect(vm.crud.formModal.visible.value).toBe(true)
    expect(vm.crud.formModal.mode.value).toBe('view')
    expect(eftSection.getById).toHaveBeenCalledWith('eb1')
    expect(vm.viewed?.id).toBe('eb1')
  })

  it('restores a shared ?detail=view:<id> for a batch that is not on the loaded page', async () => {
    routeState.query = { detail: 'view:eb9' }
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as EftVm
    expect(eftSection.getById).toHaveBeenCalledWith('eb9')
    expect(vm.crud.formModal.visible.value).toBe(true)
    expect(vm.crud.formModal.mode.value).toBe('view')
    expect(vm.viewed?.lines).toHaveLength(1)
    // The drawer lives in the Batches tab; a link without `?section=` must land there.
    expect(vm.activeSection).toBe('batches')
    expect(wrapper.text() + document.body.textContent).toContain('EFT-0009')
  })

  // The EFT batch list endpoint has no keyword, so a search box would be
  // shown and silently ignored: typing a reference returned every row.
  it('★ shows no keyword search box', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    ;(wrapper.vm as unknown as { activeSection: string }).activeSection = 'batches'
    await flushPromises()
    // The batch list sits in a tab with its header off: search lives in the toolbar.
    expect(wrapper.find('.t-list-shell__toolbar').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__inline-keyword').exists()).toBe(false)
  })

  // The payable queue is global and mixes NACHA (USD) and CPA-005 (CAD)
  // payments. Its total was one sum labelled with the first row's currency.
  it('★ the queue total lists each currency on its own', async () => {
    queue.mockResolvedValueOnce([
      { paymentEntryId: 'p1', paymentNumber: 'PMT-001', partyType: 'Vendor', partyId: 'v1', payeeName: 'Acme', docDate: '2026-03-05', currency: 'USD', amount: 1000, partyBankAccountId: 'pba1', partyBankAccountMasked: '6789', partyScheme: 'UsAba' },
      { paymentEntryId: 'p2', paymentNumber: 'PMT-002', partyType: 'Vendor', partyId: 'v2', payeeName: 'Maple', docDate: '2026-03-05', currency: 'CAD', amount: 500, partyBankAccountId: 'pba2', partyBankAccountMasked: '1234', partyScheme: 'CaPad' },
    ])
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const values = wrapper.findAllComponents({ name: 'TKpiCard' }).map((c) => c.props('value'))
    expect(values).toContain('500.00 CAD · 1,000.00 USD')
    expect(values.some((v) => String(v).includes('1,500.00'))).toBe(false)
  })

  it('mounts and loads batches + queue', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchBatches.mock.calls.length).toBeGreaterThan(0)
    expect(queue.mock.calls.length).toBeGreaterThan(0)
  })

  it('a failed queue read says why where the rows would be, never "nothing to pay"', async () => {
    queue.mockRejectedValueOnce(new Error('The payable queue is unavailable.'))
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const emptyTexts = wrapper.findAllComponents({ name: 'TResponsiveTable' }).map((t) => t.props('emptyText'))
    expect(emptyTexts).toContain('The payable queue is unavailable.')
  })

  /**
   * Voiding a batch whose file was handed out needs an explicit "never
   * submitted" acknowledgement, or the backend answers 409. The modal decided
   * whether to ask from the list row, which a download did not refresh: void
   * right after downloading showed no checkbox and dead-ended in the 409. A
   * void also never re-read the queue its payments went back to.
   */
  describe('voiding a generated batch', () => {
    type VoidVm = EftVm & {
      voidHandedOutAt: string | null
      voidAcknowledged: boolean
      submitVoid: () => Promise<void>
    }
    const generated = { id: 'eb2', number: 'EFT-0002', status: 'Generated', firstDownloadedTime: null, downloadCount: 0 }

    it('★ a download re-reads the list, so the row knows its file was handed out', async () => {
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      fetchBatches.mockClear()

      const vm = wrapper.vm as unknown as EftVm
      vm.rowActions.find((a) => a.key === 'download')!.onClick(generated)
      await flushPromises()

      expect(eftSection.download).toHaveBeenCalledWith('eb2')
      expect(fetchBatches).toHaveBeenCalled()
    })

    it('★ the void modal asks for the acknowledgement from the batch as it is now, not the row', async () => {
      eftSection.getById.mockImplementationOnce(async () => ({ ...generated, firstDownloadedTime: '2026-03-12T10:00:00Z', downloadCount: 1, lines: [] }) as never)
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()

      const vm = wrapper.vm as unknown as VoidVm
      vm.rowActions.find((a) => a.key === 'void')!.onClick(generated)
      await flushPromises()

      expect(eftSection.getById).toHaveBeenCalledWith('eb2')
      expect(vm.voidHandedOutAt).not.toBeNull()
    })

    it('★ a void re-reads the payable queue its payments went back to', async () => {
      const wrapper = mount(Page, { global: { stubs } })
      await flushPromises()
      queue.mockClear()

      const vm = wrapper.vm as unknown as VoidVm
      vm.rowActions.find((a) => a.key === 'void')!.onClick(generated)
      await flushPromises()
      await vm.submitVoid()
      await flushPromises()

      expect(eftSection.voidBatch).toHaveBeenCalledWith('eb2', expect.anything())
      expect(queue).toHaveBeenCalled()
    })
  })

  /**
   * A payment can only be sent from the bank account of the funds account it was
   * posted against (the backend refuses any other): the picker offers just that
   * profile, preselects it, and offers nothing for a mixed selection.
   */
  it('offers only the bank profile on the selected payments\' funds account', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as EftVm

    vm.onCheckedChange(['p1'])
    await flushPromises()
    expect(vm.bankAccountOptions.map((o) => o.value)).toEqual(['ba1'])
    vm.openCreate()
    await flushPromises()
    expect(vm.createForm.bankAccountId).toBe('ba1')

    vm.onCheckedChange(['p1', 'p2'])
    await flushPromises()
    expect(vm.bankAccountOptions).toEqual([])
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
