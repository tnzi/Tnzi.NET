import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Estimates and purchase orders are line-item documents: both open the
 * document editor in its line-item ("sales") shape.
 *
 * The purchase order used to open it in the expense shape, which has no
 * quantity or price columns and refuses to save without a "paid from" account
 * that a purchase order never has. A purchase order could not be saved without
 * picking that account, and when it was, every line went out with unit price 0.
 */
const h = vi.hoisted(() => ({
  createDraft: vi.fn(),
  getById: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {}, params: {}, path: '/admin/finance/purchases/purchase-orders',
    fullPath: '/admin/finance/purchases/purchase-orders', hash: '', name: 'finance.purchaseOrders', meta: {},
  }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  const emptyPage = { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }
  const offer = {
    fetch: vi.fn(async () => emptyPage),
    getById: h.getById,
    createDraft: h.createDraft,
    update: vi.fn(),
    deleteDraft: vi.fn(),
    send: vi.fn(),
    accept: vi.fn(),
    decline: vi.fn(),
    close: vi.fn(),
    convert: vi.fn(),
  }
  return {
    ...original,
    createFinanceBridge: () => ({
      estimates: offer,
      purchaseOrders: offer,
      accounts: { tree: vi.fn(async () => []), fetch: vi.fn(async () => emptyPage) },
      customers: { fetch: vi.fn(async () => emptyPage) },
      vendors: { fetch: vi.fn(async () => emptyPage) },
      items: { fetch: vi.fn(async () => emptyPage) },
      taxes: { codes: vi.fn(async () => []) },
    }),
  }
})

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
  DocumentEditor: { name: 'DocumentEditor', props: ['kind', 'entry'], template: '<div class="document-editor-stub" />' },
}

describe('Offer document editor shape', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.createDraft.mockReset().mockResolvedValue({ id: 'po1' })
    h.getById.mockReset().mockResolvedValue({ id: 'po1', status: 'Draft', lines: [] })
  })

  for (const kind of ['estimate', 'purchaseOrder'] as const) {
    it(`★ a ${kind} opens the line-item editor`, async () => {
      const wrapper = mount(Page, { props: { kind }, global: { stubs } })
      await flushPromises()

      await (wrapper.vm as unknown as { openCreate: () => Promise<void> }).openCreate()
      await flushPromises()

      const editor = wrapper.findComponent({ name: 'DocumentEditor' })
      expect(editor.exists()).toBe(true)
      expect(editor.props('kind')).toBe('sales')
    })
  }

  it('saves a purchase order with the quantity and unit price entered on each line', async () => {
    const wrapper = mount(Page, { props: { kind: 'purchaseOrder' as const }, global: { stubs } })
    await flushPromises()

    await (wrapper.vm as unknown as { saveEditor: (p: unknown, send: boolean) => Promise<void> }).saveEditor({
      partyId: 'vendor-1', paidFromAccountId: null, paymentMethod: null, docDate: '2026-07-20', dueDate: null,
      currency: 'USD', exchangeRate: null, memo: null,
      lines: [{ itemId: null, description: 'Widgets', accountId: 'a1', quantity: 2, unitPrice: 120, amount: 240, taxCodeId: null, taxAmount: null }],
    }, false)

    expect(h.createDraft).toHaveBeenCalledWith(expect.objectContaining({
      vendorId: 'vendor-1',
      lines: [expect.objectContaining({ quantity: 2, unitPrice: 120 })],
    }))
  })
})
