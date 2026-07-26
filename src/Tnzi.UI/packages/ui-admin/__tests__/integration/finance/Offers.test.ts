import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Estimates / purchase orders - the two non-posting documents.
 *
 * What is worth locking: the lifecycle actions offered on a row depend on the
 * status (a converted document must not still offer "Send"), and converting
 * lands the operator on the draft that was created rather than back on a list.
 */
const h = vi.hoisted(() => ({
  push: vi.fn(),
  fetch: vi.fn(),
  send: vi.fn(),
  accept: vi.fn(),
  decline: vi.fn(),
  close: vi.fn(),
  convert: vi.fn(),
  getById: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {}, params: {}, path: '/admin/finance/sales/estimates',
    fullPath: '/admin/finance/sales/estimates', hash: '', name: 'finance.estimates', meta: {},
  }),
  useRouter: () => ({ push: h.push, replace: vi.fn(), back: vi.fn() }),
}))

const rows = [
  { id: 'e1', number: null, status: 'Draft', customerName: 'Acme', docDate: '2026-07-20', currency: 'USD', total: 1200 },
  { id: 'e2', number: 'EST-000002', status: 'Sent', customerName: 'Acme', docDate: '2026-07-18', expiryDate: '2026-08-18', currency: 'USD', total: 800 },
  { id: 'e3', number: 'EST-000003', status: 'Converted', customerName: 'Beta', docDate: '2026-07-10', currency: 'USD', total: 500, convertedToDocType: 'Invoice', convertedToDocId: 'i9' },
]

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  const offer = {
    fetch: h.fetch,
    getById: h.getById,
    createDraft: vi.fn(),
    update: vi.fn(),
    deleteDraft: vi.fn(),
    send: h.send,
    accept: h.accept,
    decline: h.decline,
    close: h.close,
    convert: h.convert,
  }
  return {
    ...original,
    createFinanceBridge: () => ({
      estimates: offer,
      purchaseOrders: offer,
      accounts: { tree: vi.fn(async () => []), fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      customers: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      vendors: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      items: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
      taxes: { codes: vi.fn(async () => []) },
    }),
  }
})

// Mounted directly rather than through `Estimates.vue`: that file is a
// two-line wrapper, and `wrapper.vm` would expose the wrapper, not the page.
import Page from '../../../src/pages/finance/components/OfferPage.vue'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data', 'columns'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div />' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Descriptions: { name: 'Descriptions', template: '<div><slot /></div>' },
  DescriptionsItem: { name: 'DescriptionsItem', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Alert: { name: 'Alert', template: '<div><slot /></div>' },
}

describe('Finance offer documents', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.push.mockReset()
    h.fetch.mockReset().mockResolvedValue({ items: rows, totalCount: 3, pageIndex: 1, pageSize: 20 })
    h.getById.mockReset().mockResolvedValue(rows[1])
    h.send.mockReset().mockResolvedValue(rows[1])
    h.convert.mockReset().mockResolvedValue({ sourceId: 'e2', sourceNumber: 'EST-000002', docType: 'Invoice', docId: 'i7' })
  })

  it('mounts and loads its data', async () => {
    mount(Page, { props: { kind: 'estimate' as const }, global: { stubs } })
    await flushPromises()
    expect(h.fetch).toHaveBeenCalled()
  })

  it('offers only the actions the status allows', async () => {
    const wrapper = mount(Page, { props: { kind: 'estimate' as const }, global: { stubs } })
    await flushPromises()

    const actions = wrapper.vm.rowActions as Array<{ key: string; show?: (r: unknown) => boolean }>
    const visible = (row: unknown) => actions.filter((a) => !a.show || a.show(row)).map((a) => a.key)

    // A draft has not been sent, so there is nothing to accept or convert yet.
    expect(visible(rows[0])).toEqual(['send', 'edit', 'delete'])

    // A sent estimate is the one that can move forward - or be turned down.
    expect(visible(rows[1])).toEqual(['accept', 'convert', 'decline', 'close', 'edit'])

    // A converted estimate is history: no lifecycle actions, and crucially no
    // delete (the customer holds a copy of that number).
    expect(visible(rows[2])).toEqual([])
  })

  it('lands on the draft it created after converting', async () => {
    const wrapper = mount(Page, { props: { kind: 'estimate' as const }, global: { stubs } })
    await flushPromises()

    const convert = (wrapper.vm.rowActions as Array<{ key: string; onClick: (r: unknown) => void }>).find((a) => a.key === 'convert')!
    convert.onClick(rows[1])
    await flushPromises()

    await (wrapper.vm as unknown as { runConvert: () => Promise<void> }).runConvert()
    await flushPromises()

    expect(h.convert).toHaveBeenCalledWith('e2', expect.objectContaining({ docDate: expect.any(String) }))
    expect(h.push).toHaveBeenCalledWith({ name: 'finance.invoices', query: { detail: 'view:i7' } })
  })

  it('drills a converted estimate through to the invoice it became', async () => {
    const wrapper = mount(Page, { props: { kind: 'estimate' as const }, global: { stubs } })
    await flushPromises()

    ;(wrapper.vm as unknown as { openTarget: (r: unknown) => void }).openTarget(rows[2])

    expect(h.push).toHaveBeenCalledWith({ name: 'finance.invoices', query: { detail: 'view:i9' } })
  })
})
