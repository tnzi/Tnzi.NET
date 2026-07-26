import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Recurring templates page - schedule preview, lifecycle gating, generation.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/recurring', fullPath: '/admin/finance/recurring', hash: '', name: 'finance.recurring', meta: {} }),
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
  Select: { name: 'Select', template: '<select />' },
  Input: { name: 'Input', template: '<input />' },
  InputNumber: { name: 'InputNumber', template: '<input type="number" />' },
  DatePicker: { name: 'DatePicker', template: '<input type="date" />' },
  Spin: { name: 'Spin', template: '<div><slot /></div>' },
  Tag: { name: 'Tag', template: '<span><slot /></span>' },
}

interface RecurringVm {
  rowActions: Array<{ key: string; show?: (row: Record<string, unknown>) => boolean }>
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
  })

  it('mounts and loads its data', async () => {
    mount(Page, { global: { stubs } })
    await flushPromises()
    expect(fetchList.mock.calls.length).toBeGreaterThan(0)
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
})
