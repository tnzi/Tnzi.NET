import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Customer / vendor work surface.
 *
 * What is worth locking here is not that it renders - it is the three things
 * that make it a work surface rather than a read-only card: the balance is the
 * backend's (never re-summed in the client), every row drills through to its
 * source document, and the context actions carry their party with them.
 */
// `vi.mock` factories are hoisted above every top-level const, so the spies the
// factory closes over have to be hoisted with it.
const h = vi.hoisted(() => ({
  push: vi.fn(),
  getParty: vi.fn(),
  getSummary: vi.fn(),
  getTransactions: vi.fn(),
}))
const { push, getParty, getSummary, getTransactions } = h

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {}, params: { id: 'c1' }, path: '/admin/finance/sales/customers/c1',
    fullPath: '/admin/finance/sales/customers/c1', hash: '', name: 'finance.customers.detail', meta: {}, matched: [],
  }),
  useRouter: () => ({ push: h.push, replace: vi.fn(), back: vi.fn(), resolve: () => ({ matched: [] }) }),
}))

const party = { id: 'c1', name: 'Acme Supplies Ltd', code: 'ACME', email: 'ap@acme.test', currency: 'CAD', paymentTermsDays: 30, isActive: true }
const summary = {
  partyId: 'c1',
  partyName: 'Acme Supplies Ltd',
  partyType: 0,
  baseCurrency: 'CAD',
  openBalance: 2040,
  overdue: 840,
  buckets: { current: 1200, days1To30: 0, days31To60: 840, days61To90: 0, over90: 0, total: 2040 },
  periodTotal: 2040,
  periodFrom: '2026-07-01',
  periodTo: '2026-07-25',
  openDocumentCount: 2,
  lastTransactionDate: '2026-07-15',
}
const entries = [
  { docType: 'PaymentEntry', docId: 'p1', number: 'PMT-000004', docDate: '2026-07-15', currency: 'CAD', amount: -400, outstanding: 0, status: 'Posted', overdueDays: 0 },
  { docType: 'Invoice', docId: 'i1', number: 'INV-000009', docDate: '2026-07-05', dueDate: '2026-08-04', currency: 'CAD', amount: 1200, outstanding: 1200, status: 'Posted', overdueDays: 0 },
]

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  const partySection = { get: h.getParty, summary: h.getSummary, transactions: h.getTransactions, fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() }
  return {
    ...original,
    createFinanceBridge: () => ({
      customers: partySection,
      vendors: partySection,
      partyBankAccounts: { byParty: vi.fn(async () => []), save: vi.fn(), update: vi.fn(), setDefault: vi.fn(), delete: vi.fn() },
    }),
  }
})

import Page from '../../../src/pages/finance/CustomerDetail.vue'
import { fmtMoney } from '../../../src/pages/finance/money'

const stubs = {
  Card: { name: 'Card', template: '<div><slot /></div>' },
  DataTable: {
    name: 'DataTable',
    props: ['data', 'columns'],
    template: '<div class="n-data-table-stub" />',
  },
  Pagination: { name: 'Pagination', template: '<div />' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Descriptions: { name: 'Descriptions', template: '<div><slot /></div>' },
  DescriptionsItem: { name: 'DescriptionsItem', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  TabPane: { name: 'TabPane', template: '<div><slot /></div>' },
}

describe('Finance party work surface', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockReset()
    getParty.mockReset().mockResolvedValue(party)
    getSummary.mockReset().mockResolvedValue(summary)
    getTransactions.mockReset().mockResolvedValue({ items: entries, totalCount: 2, pageIndex: 1, pageSize: 20 })
  })

  it('loads the record, its summary and its ledger', async () => {
    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()

    expect(getParty).toHaveBeenCalledWith('c1')
    expect(getSummary).toHaveBeenCalledWith('c1')
    expect(getTransactions).toHaveBeenCalledWith('c1', expect.objectContaining({ pageIndex: 1 }))
    expect(wrapper.text()).toContain('Acme Supplies Ltd')
  })

  // A failed summary read rendered both money cards as a dash in the success
  // tone: a green "nothing owed, nothing overdue" over figures it never read.
  it('★ a failed summary read leaves the balance cards neutral', async () => {
    getSummary.mockRejectedValue(new Error('Request failed with status code 500'))
    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()

    const tones = wrapper.findAllComponents({ name: 'TKpiCard' }).map((c) => c.props('tone'))
    expect(tones).not.toContain('success')
    expect(tones).not.toContain('error')
  })

  it('shows the balance the backend computed, in the accounting format', async () => {
    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()

    // $2,040.00 - the aging report's figure, not a sum of the visible page.
    expect(wrapper.text()).toContain('2,040.00')
    expect(wrapper.text()).toContain('840.00')
  })

  it('labels the balance strip with the ledger base currency, not the party currency', async () => {
    // The summary figures are converted to the base currency (the same basis
    // as the aging bar under them); the party currency is only what its
    // documents are written in.
    getParty.mockResolvedValue({ ...party, currency: 'EUR' })
    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()

    const values = wrapper
      .findAllComponents({ name: 'TKpiCard' })
      .map((c) => c.props('value'))
      .filter((v): v is string => typeof v === 'string')
    expect(values).toEqual([fmtMoney(2040, 'CAD'), fmtMoney(840, 'CAD'), fmtMoney(2040, 'CAD')])
    expect(values.join(' ')).not.toContain('€')
  })

  it('drills a ledger row through to its source document', async () => {
    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()

    const list = wrapper.findComponent({ name: 'TTransactionList' })
    list.vm.$emit('open', entries[1])
    await flushPromises()

    expect(push).toHaveBeenCalledWith({ name: 'finance.invoices', query: { detail: 'view:i1' } })
  })

  it('hands the party to the document and payment actions', async () => {
    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()

    const buttons = wrapper.findAll('button')
    const invoice = buttons.find((b) => b.text().includes('New Invoice'))
    const payment = buttons.find((b) => b.text().includes('Receive Payment'))
    expect(invoice).toBeTruthy()
    expect(payment).toBeTruthy()

    await invoice!.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'finance.invoices', query: { entry: 'new', party: 'c1' } })

    await payment!.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'finance.payments', query: { party: 'c1', direction: 'Inbound' } })
  })

  it('asks the backend for open-only documents rather than filtering the page', async () => {
    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()
    getTransactions.mockClear()

    // The overview renders a preview list too; the filterable one is the
    // ledger on the transactions tab.
    const ledger = wrapper.findAllComponents({ name: 'TTransactionList' }).find((c) => c.props('showScope') !== false)
    ledger!.vm.$emit('update:scope', 'open')
    await flushPromises()

    expect(getTransactions).toHaveBeenCalledWith('c1', expect.objectContaining({ openOnly: true, pageIndex: 1 }))
  })

  // The route reuses this page from one party to the next. On a slow link the
  // first party's answers arriving last must not paint its name, balance and
  // ledger under the party now on screen.
  it('★ keeps the party navigated to last when an earlier read answers late', async () => {
    const answers = new Map<string, Array<() => void>>()
    const later = <T,>(id: string, value: T) =>
      new Promise<T>((resolve) => { answers.set(id, [...(answers.get(id) ?? []), () => resolve(value)]) })
    getParty.mockImplementation((id: string) => later(id, { ...party, id, name: id === 'c1' ? 'Acme Supplies Ltd' : 'Beta Traders Inc' }))
    getSummary.mockImplementation((id: string) => later(id, { ...summary, partyId: id, openBalance: id === 'c1' ? 2040 : 75 }))
    getTransactions.mockImplementation((id: string) =>
      later(id, { items: id === 'c1' ? entries : [], totalCount: id === 'c1' ? 2 : 0, pageIndex: 1, pageSize: 20 }),
    )

    const wrapper = mount(Page, { props: { id: 'c1' }, global: { stubs } })
    await flushPromises()
    await wrapper.setProps({ id: 'c2' })
    await flushPromises()

    for (const answer of answers.get('c2') ?? []) answer()
    await flushPromises()
    for (const answer of answers.get('c1') ?? []) answer()
    await flushPromises()

    const surface = wrapper.findComponent({ name: 'PartyWorkSurface' }).vm as unknown as {
      party: { name: string } | null
      summary: { partyId: string } | null
      entries: unknown[]
    }
    expect(surface.party?.name).toBe('Beta Traders Inc')
    expect(surface.summary?.partyId).toBe('c2')
    expect(surface.entries).toEqual([])
  })
})
