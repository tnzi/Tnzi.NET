import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive, watch } from 'vue'

/**
 * Recurring templates page - schedule preview, lifecycle gating, generation.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

// Mutable so a test can arrive with a deep link in the URL.
const routeQuery = vi.hoisted(() => ({ current: {} as Record<string, string> }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery.current, params: {}, path: '/admin/finance/recurring', fullPath: '/admin/finance/recurring', hash: '', name: 'finance.recurring', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const fetchList = vi.fn(async () => ({
  items: [
    {
      id: 'r1', name: 'Monthly retainer', kind: 'Invoice', status: 'Active', partyId: 'c1', partyName: 'Acme',
      frequency: 'Monthly', interval: 1, anchorDay: 15, startDate: '2026-01-15', nextRunDate: '2026-08-15',
      occurrenceCount: 7, estimatedTotal: 500, effectiveAutoPost: false, currency: 'USD', lines: [], concurrencyStamp: 's',
    },
    {
      id: 'r2', name: 'Paused rent', kind: 'Bill', status: 'Paused', partyId: 'v1', partyName: 'Landlord',
      frequency: 'Monthly', interval: 1, startDate: '2026-01-01', nextRunDate: '2026-08-01',
      occurrenceCount: 7, estimatedTotal: 1200, effectiveAutoPost: true, currency: 'USD', lines: [], concurrencyStamp: 's',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

const previewSchedule = vi.fn(async () => ({ dates: ['2026-08-15', '2026-09-15', '2026-10-15'] }))
const runDue = vi.fn(async () => ({ templatesDue: 2, generated: 2, skipped: 0, failed: 0, runs: [] }))
const runs = vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 50 }))

const recurringSection = {
  fetch: fetchList,
  getById: vi.fn(async () => null),
  create: vi.fn(),
  update: vi.fn(),
  delete: vi.fn(),
  pause: vi.fn(async () => ({})),
  resume: vi.fn(async () => ({})),
  end: vi.fn(async () => ({})),
  preview: vi.fn(async () => ({ dates: [] })),
  previewSchedule,
  runs,
  run: vi.fn(async () => ({ templatesDue: 1, generated: 1, skipped: 0, failed: 0, runs: [] })),
  runDue,
}

vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => {
  const original = await importOriginal<Record<string, unknown>>()
  return {
    FinanceDocumentStatus: original.FinanceDocumentStatus,
    CashFlowActivity: original.CashFlowActivity,
    createFinanceBridge: () => ({
      accounts: { tree: vi.fn(async () => []) },
      customers: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })) },
      vendors: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })) },
      items: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })) },
      taxes: { fetchCodes: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })), fetchRates: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })), fetchAgencies: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })) },
      recurring: recurringSection,
    }),
  }
})

import Page from '../../../src/pages/finance/Recurring.vue'

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
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  Tag: { name: 'Tag', template: '<span><slot /></span>' },
}

interface RecurringVm {
  rowActions: Array<{ key: string; label: string; confirm?: string; show?: (row: Record<string, unknown>) => boolean }>
  tp: (key: string) => string
  previewSchedule: (model: Record<string, unknown>) => Promise<void>
  previewDates: string[]
  runDue: () => Promise<void>
}

describe('Finance Recurring page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchList.mockClear()
    previewSchedule.mockClear()
    runDue.mockClear()
    recurringSection.getById.mockReset().mockImplementation(async () => null)
    routeQuery.current = {}
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
  })

  /**
   * The row actions are declared as i18n keys and rendered through the page
   * translator, which prepends `admin.modules.finance.recurring.` itself. A key
   * that already carries the namespace misses the dictionary and is humanised:
   * "End Confirm" in the popconfirm and English labels in every locale. Resolve
   * the declared keys through the page's own translator, not just their `show`.
   */
  it('row action labels and the end confirm resolve to their dictionary strings', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as RecurringVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    expect(vm.tp(byKey.end!.confirm!)).toContain('stops generating')
    expect(vm.tp(byKey.run!.label)).toBe('Run now')
    expect(vm.tp(byKey.pause!.label)).toBe('Pause')
    expect(vm.tp(byKey.resume!.label)).toBe('Resume')
    expect(vm.tp(byKey.history!.label)).toBe('History')
    expect(vm.tp(byKey.end!.label)).toBe('End')
  })

  /** Pause/resume are mutually exclusive; a spent template offers neither. */
  it('gates the lifecycle actions on status', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as RecurringVm
    const byKey = Object.fromEntries(vm.rowActions.map((a) => [a.key, a]))
    const active = { id: 'r1', status: 'Active' }
    const paused = { id: 'r2', status: 'Paused' }
    const ended = { id: 'r3', status: 'Ended' }

    expect(byKey.pause!.show!(active)).toBe(true)
    expect(byKey.pause!.show!(paused)).toBe(false)
    expect(byKey.resume!.show!(paused)).toBe(true)
    expect(byKey.resume!.show!(active)).toBe(false)
    expect(byKey.end!.show!(ended)).toBe(false)
    // Running by hand only makes sense while the schedule is live.
    expect(byKey.run!.show!(active)).toBe(true)
    expect(byKey.run!.show!(paused)).toBe(false)
  })

  /**
   * The preview is what stops "anchor 31, quarterly" from billing on the wrong
   * day, so it must reach the server with the schedule as entered.
   */
  it('previews the next dates before anything is saved', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as RecurringVm
    await vm.previewSchedule({ name: 'X', kind: 'Invoice', partyId: 'c1', frequency: 'Monthly', interval: 1, anchorDay: 15, startDate: '2026-08-15', lines: [] })
    await flushPromises()

    expect(previewSchedule).toHaveBeenCalled()
    const payload = previewSchedule.mock.calls[0]![0] as Record<string, unknown>
    expect(payload.anchorDay).toBe(15)
    expect(payload.frequency).toBe('Monthly')
    expect(vm.previewDates).toEqual(['2026-08-15', '2026-09-15', '2026-10-15'])
  })

  /** No start date = nothing to preview; it must not fire a pointless request. */
  it('skips the preview when the schedule is incomplete', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as RecurringVm
    await vm.previewSchedule({ name: 'X' })
    expect(previewSchedule).not.toHaveBeenCalled()
    expect(vm.previewDates).toEqual([])
  })

  it('runs the whole sweep and reloads', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const before = fetchList.mock.calls.length
    await (wrapper.vm as unknown as RecurringVm).runDue()
    await flushPromises()

    expect(runDue).toHaveBeenCalled()
    expect(fetchList.mock.calls.length).toBeGreaterThan(before)
  })

  it('a failed history read says so instead of "nothing generated yet"', async () => {
    // The history drawer is "the only place that answers did it actually run";
    // an empty list after a failed read answers "no" without having asked.
    runs.mockRejectedValueOnce(new Error('History is unavailable.'))
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    await (wrapper.vm as unknown as { openHistory: (id?: string) => Promise<void> }).openHistory()
    await flushPromises()

    const vm = wrapper.vm as unknown as { runsError: string; runs: unknown[] }
    expect(vm.runsError).toBe('History is unavailable.')
    expect(vm.runs).toEqual([])
  })

  // The date picker answers with an epoch number; `startDate` / `endDate` bind
  // to DateTime, so a number in the body fails the request (400) and the
  // template is never saved. The picked day goes out as `YYYY-MM-DD`.
  it('sends picked start and end dates as calendar dates', async () => {
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as RecurringVm
    await vm.previewSchedule({
      name: 'Rent', kind: 'Bill', partyId: 'v1', frequency: 'Monthly', interval: 1,
      startDate: new Date(2026, 10, 1).getTime(), endDate: new Date(2027, 9, 31).getTime(),
      lines: [{ description: 'Rent', accountId: 'a1', quantity: 1, unitPrice: 1200 }],
    })
    await flushPromises()

    const payload = previewSchedule.mock.calls[0]![0] as Record<string, unknown>
    expect(payload.startDate).toBe('2026-11-01')
    expect(payload.endDate).toBe('2027-10-31')
  })

  it('a failed preview says why instead of showing no dates', async () => {
    previewSchedule.mockRejectedValueOnce(new Error('Interval must be at least 1.'))
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as RecurringVm & { previewError: string }
    await vm.previewSchedule({ name: 'X', kind: 'Invoice', partyId: 'c1', frequency: 'Monthly', interval: 0, startDate: '2026-08-15', lines: [] })
    await flushPromises()

    expect(vm.previewDates).toEqual([])
    expect(vm.previewError).toBe('Interval must be at least 1.')
  })

  // The list projection carries no lines. Editing from the row opened an empty
  // lines editor: saving was rejected ("needs at least one line") or forced the
  // lines to be retyped from memory, replacing the originals.
  it('edit opens on the full template, lines included, and saves them back', async () => {
    recurringSection.getById.mockResolvedValueOnce({
      id: 'r1', name: 'Monthly retainer', kind: 'Invoice', status: 'Active', partyId: 'c1', currency: 'USD',
      paymentMethod: 'ACH', frequency: 'Monthly', interval: 1, anchorDay: 15,
      startDate: '2026-01-15T00:00:00Z', endDate: null, autoPost: null, concurrencyStamp: 's',
      lines: [
        { id: 'l1', lineNumber: 1, itemId: null, description: 'Retainer', accountId: 'a1', quantity: 1, unitPrice: 500, taxCodeId: 't1', amount: 500 },
      ],
    } as never)
    recurringSection.update.mockResolvedValueOnce({ id: 'r1' } as never)
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    const vm = wrapper.vm as unknown as RecurringVm & {
      crud: { formModal: { formData: { value: Record<string, unknown> | null } }; submit: () => Promise<unknown> }
    }
    // Every record the form shows, in order: it must open on the full template,
    // not flash the bare row first.
    const shown: number[] = []
    watch(() => vm.crud.formModal.formData.value, (d) => {
      if (d) shown.push((d.lines as unknown[] | undefined)?.length ?? 0)
    }, { flush: 'sync' })
    const edit = vm.rowActions.find((a) => a.key === 'edit') as unknown as { onClick: (row: Record<string, unknown>) => unknown }
    // The row as the list hands it over: no lines.
    await edit.onClick({ id: 'r1', name: 'Monthly retainer', kind: 'Invoice', status: 'Active', partyId: 'c1', lines: [], concurrencyStamp: 's' })
    await flushPromises()

    expect(recurringSection.getById).toHaveBeenCalledWith('r1')
    expect(shown).toEqual([1])
    expect((vm.crud.formModal.formData.value?.lines as unknown[]).length).toBe(1)

    await vm.crud.submit()
    const [id, body] = recurringSection.update.mock.calls.at(-1)! as unknown as [string, Record<string, unknown>]
    expect(id).toBe('r1')
    expect(body.lines).toEqual([
      { itemId: null, description: 'Retainer', accountId: 'a1', quantity: 1, unitPrice: 500, taxCodeId: 't1' },
    ])
    // Not on the form, but replaced on save: a value set through the API stays.
    expect(body.paymentMethod).toBe('ACH')
    expect(body.startDate).toBe('2026-01-15T00:00:00Z')
  })

  // A refresh while editing restores `?detail=edit:r1` from the loaded list,
  // whose row has no lines: the form must still end up on the full template.
  it('a restored edit deep link also opens on the full template', async () => {
    recurringSection.getById.mockResolvedValue({
      id: 'r1', name: 'Monthly retainer', kind: 'Invoice', status: 'Active', partyId: 'c1', frequency: 'Monthly', interval: 1,
      startDate: '2026-01-15T00:00:00Z', concurrencyStamp: 's',
      lines: [{ id: 'l1', lineNumber: 1, description: 'Retainer', accountId: 'a1', quantity: 1, unitPrice: 500, amount: 500 }],
    } as never)
    routeQuery.current = { detail: 'edit:r1' }
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    await flushPromises()

    const vm = wrapper.vm as unknown as { crud: { formModal: { mode: { value: string | null }; formData: { value: Record<string, unknown> | null } } } }
    expect(vm.crud.formModal.mode.value).toBe('edit')
    expect(recurringSection.getById).toHaveBeenCalledWith('r1')
    expect((vm.crud.formModal.formData.value?.lines as unknown[]).length).toBe(1)
  })

  // Back / Forward onto an edit link while the list is on screen resolves the
  // record from the loaded rows, which carry no lines: the form must still end
  // up on the full template.
  it('an edit link reached with the list loaded also opens on the full template', async () => {
    recurringSection.getById.mockResolvedValue({
      id: 'r1', name: 'Monthly retainer', kind: 'Invoice', status: 'Active', partyId: 'c1', frequency: 'Monthly', interval: 1,
      startDate: '2026-01-15T00:00:00Z', concurrencyStamp: 's',
      lines: [{ id: 'l1', lineNumber: 1, description: 'Retainer', accountId: 'a1', quantity: 1, unitPrice: 500, amount: 500 }],
    } as never)
    routeQuery.current = reactive({}) as Record<string, string>
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    expect(recurringSection.getById).not.toHaveBeenCalled()

    routeQuery.current.detail = 'edit:r1'
    await flushPromises()
    await flushPromises()

    const vm = wrapper.vm as unknown as { crud: { formModal: { mode: { value: string | null }; formData: { value: Record<string, unknown> | null } } } }
    expect(vm.crud.formModal.mode.value).toBe('edit')
    expect(recurringSection.getById).toHaveBeenCalledWith('r1')
    expect((vm.crud.formModal.formData.value?.lines as unknown[]).length).toBe(1)
  })
})
