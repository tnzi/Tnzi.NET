import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { Ref } from 'vue'

/**
 * Journal Entries page - read-only TCrudPage with a `#detail` drawer
 * (onView lazy-loads the full entry), a useDetail-driven line-editor
 * overlay (TDetailHost), and post/reverse/delete lifecycle row actions.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/journal-entries', fullPath: '/admin/finance/journal-entries', hash: '', name: 'finance.journals', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const entry = {
  id: 'j1',
  number: 'JE-000001',
  status: 'Posted',
  postingDate: '2026-03-15T00:00:00',
  memo: 'Test sale',
  currency: 'USD',
  exchangeRate: 1,
  totalDebit: 100,
  totalCredit: 100,
  lines: [
    { id: 'l1', lineNumber: 1, accountId: 'a1', accountCode: '1200', accountName: 'AR', debit: 100, credit: 0, txnDebit: 100, txnCredit: 0, currency: 'USD' },
    { id: 'l2', lineNumber: 2, accountId: 'a2', accountCode: '4100', accountName: 'Sales', debit: 0, credit: 100, txnDebit: 0, txnCredit: 100, currency: 'USD' },
  ],
}

const journalsFetch = vi.fn(async () => ({ items: [entry], totalCount: 1, pageIndex: 1, pageSize: 20 }))
const getById = vi.fn(async () => entry)
const postEntry = vi.fn(async () => entry)
const reverseEntry = vi.fn(async () => ({ ...entry, id: 'j2', number: 'JE-000002' }))

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    // Keep the real JournalEntryStatus enum re-export.
    JournalEntryStatus: original.JournalEntryStatus,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      journals: {
        fetch: journalsFetch,
        getById,
        createDraft: vi.fn(async () => entry),
        updateDraft: vi.fn(async () => entry),
        deleteDraft: vi.fn(async () => undefined),
        post: postEntry,
        reverse: reverseEntry,
      },
    }),
  }
})

import JournalEntries from '../../../src/pages/finance/JournalEntries.vue'

const stubs = {
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot /></div>' },
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot /></div>' },
  Descriptions: { name: 'Descriptions', template: '<div><slot /></div>' },
  DescriptionsItem: { name: 'DescriptionsItem', template: '<div><slot /></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Select: { name: 'Select', template: '<select class="n-select-stub" />' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
}

interface JournalEntriesVm {
  openCreate: () => void
  crud: { openView: (row: { id?: string }) => void; formModal: { visible: Ref<boolean> } }
  viewed: { number?: string; lines?: unknown[] } | null
  entryDetail: { visible: Ref<boolean>; action: Ref<string | null>; close: () => void }
}

describe('Finance JournalEntries page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    journalsFetch.mockClear()
    getById.mockClear()
  })

  it('mounts and fetches the entry list', async () => {
    mount(JournalEntries, { global: { stubs } })
    await flushPromises()
    expect(journalsFetch).toHaveBeenCalledTimes(1)
  })

  it('openView lazy-loads the full entry for the #detail drawer', async () => {
    const wrapper = mount(JournalEntries, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as JournalEntriesVm
    vm.crud.openView({ id: 'j1' })
    await flushPromises()

    expect(getById).toHaveBeenCalledWith('j1')
    expect(vm.viewed?.number).toBe('JE-000001')
    expect(vm.viewed?.lines).toHaveLength(2)
  })

  it('openCreate opens the useDetail-driven line editor', async () => {
    const wrapper = mount(JournalEntries, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as JournalEntriesVm
    vm.openCreate()
    await flushPromises()

    expect(vm.entryDetail.visible.value).toBe(true)
    expect(vm.entryDetail.action.value).toBe('create')
  })

  // An entry a document posted is undone by voiding that document: the ledger
  // refuses to reverse it (409), since the document would stay Posted. Manual
  // entries and the ledger's own postings (revaluation) are reversed here.
  it('★ offers Reverse only on posted entries the ledger can reverse', async () => {
    const wrapper = mount(JournalEntries, { global: { stubs } })
    await flushPromises()
    const actions = (wrapper.vm as unknown as { rowActions: Array<{ key: string; show?: (r: unknown) => boolean }> }).rowActions
    const offersReverse = (row: unknown) => actions.some((a) => a.key === 'reverse' && (!a.show || a.show(row)))

    expect(offersReverse({ id: 'm', status: 'Posted', sourceType: null })).toBe(true)
    expect(offersReverse({ id: 'r', status: 'Posted', sourceType: 'Revaluation' })).toBe(true)
    expect(offersReverse({ id: 'a', status: 'Posted', sourceType: 'PaymentApplication' })).toBe(true)
    for (const token of ['Invoice', 'Bill', 'CreditMemo', 'Expense', 'PaymentEntry', 'Transfer', 'Deposit', 'PayRun', 'PayRun.Payment']) {
      expect(offersReverse({ id: token, status: 'Posted', sourceType: token })).toBe(false)
    }
  })

  // A new entry's first save creates a draft even when the post that follows
  // fails. The editor then continues on that draft; the page shows it as the
  // record being edited and lists it.
  it('★ a draft created by the editor becomes the record being edited and is listed', async () => {
    const wrapper = mount(JournalEntries, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as JournalEntriesVm
    vm.openCreate()
    await flushPromises()
    journalsFetch.mockClear()

    wrapper.findComponent({ name: 'JournalEntryEditor' }).vm.$emit('drafted', { ...entry, id: 'j9', status: 'Draft' })
    await flushPromises()

    expect(vm.entryDetail.action.value).toBe('edit')
    const editor = wrapper.findComponent({ name: 'JournalEntryEditor' })
    expect((editor.props('entry') as { id: string } | null)?.id).toBe('j9')
    expect(journalsFetch).toHaveBeenCalled()
  })
})
