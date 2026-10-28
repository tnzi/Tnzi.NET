import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Recurring templates: what the operator is told after Run now and Run due.
 *
 * Both answer 200 with counts even when a period failed (the failure is kept
 * on the run and retried). Run now used to read "generated 0" as "Nothing was
 * due." in a green toast, so a retry that failed again looked like there was
 * nothing to do, and every click used up one of the retries.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin/finance/recurring', fullPath: '/admin/finance/recurring', hash: '', name: 'finance.recurring', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const successMock = vi.fn()
const errorMock = vi.fn()
const warningMock = vi.fn()
const infoMock = vi.fn()
vi.mock('../../../src/pages/_shared/safe-message', () => ({
  useSafeMessage: () => ({ success: successMock, error: errorMock, warning: warningMock, info: infoMock }),
}))

type SweepResult = {
  templatesDue: number
  generated: number
  skipped: number
  failed: number
  runs: Array<Record<string, unknown>>
}

const sweep = (over: Partial<SweepResult>): SweepResult => ({ templatesDue: 1, generated: 0, skipped: 0, failed: 0, runs: [], ...over })

const failedRun = {
  id: 'run1', recurringDocumentId: 'r1', periodDate: '2026-08-15', status: 'Failed', posted: false,
  failReason: "Line 1: Account '5200' is inactive.", creationTime: '2026-08-15T00:00:00Z',
}

const run = vi.fn(async (): Promise<SweepResult> => sweep({}))
const runDue = vi.fn(async (): Promise<SweepResult> => sweep({}))

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
      taxes: {
        fetchCodes: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })),
        fetchRates: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })),
        fetchAgencies: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })),
      },
      recurring: {
        fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
        getById: vi.fn(async () => null),
        create: vi.fn(),
        update: vi.fn(),
        delete: vi.fn(),
        pause: vi.fn(async () => ({})),
        resume: vi.fn(async () => ({})),
        end: vi.fn(async () => ({})),
        preview: vi.fn(async () => ({ dates: [] })),
        previewSchedule: vi.fn(async () => ({ dates: [] })),
        runs: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 50 })),
        run,
        runDue,
      },
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
  rowActions: Array<{ key: string; onClick?: (row: Record<string, unknown>) => Promise<void> | void }>
  runDue: () => Promise<void>
}

async function runNow(result: SweepResult): Promise<void> {
  run.mockResolvedValueOnce(result)
  const wrapper = mount(Page, { global: { stubs } })
  await flushPromises()
  const vm = wrapper.vm as unknown as RecurringVm
  const action = vm.rowActions.find((a) => a.key === 'run')
  await action!.onClick!({ id: 'r1', status: 'Active' })
  await flushPromises()
}

describe('Finance Recurring page: run outcome messages', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    for (const mock of [successMock, errorMock, warningMock, infoMock, run, runDue]) mock.mockClear()
  })

  it('reports a failed period as an error with its reason, never as a success', async () => {
    await runNow(sweep({ failed: 1, runs: [failedRun] }))

    expect(successMock).not.toHaveBeenCalled()
    expect(errorMock).toHaveBeenCalledTimes(1)
    const text = String(errorMock.mock.calls[0]![0])
    expect(text).toContain('1 failed')
    expect(text).toContain("Account '5200' is inactive.")
  })

  it('reports a generated document as a success', async () => {
    await runNow(sweep({ generated: 1, runs: [{ ...failedRun, status: 'Generated', failReason: null }] }))

    expect(errorMock).not.toHaveBeenCalled()
    expect(successMock).toHaveBeenCalledWith('Generated 1.')
  })

  it('reports periods skipped by the catch-up policy with the counts', async () => {
    await runNow(sweep({ skipped: 2 }))

    expect(successMock).not.toHaveBeenCalled()
    expect(infoMock).toHaveBeenCalledTimes(1)
    expect(String(infoMock.mock.calls[0]![0])).toContain('2 skipped')
  })

  it('does not call a run that generated nothing "nothing due"', async () => {
    await runNow(sweep({}))

    expect(successMock).not.toHaveBeenCalled()
    expect(infoMock).toHaveBeenCalledTimes(1)
    const text = String(infoMock.mock.calls[0]![0])
    expect(text).toContain('Nothing new was generated')
    expect(text).not.toContain('due')
  })

  it('warns, with the first reason, when the due sweep had failures', async () => {
    runDue.mockResolvedValueOnce(sweep({ templatesDue: 2, generated: 1, failed: 1, runs: [failedRun] }))
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    await (wrapper.vm as unknown as RecurringVm).runDue()
    await flushPromises()

    expect(successMock).not.toHaveBeenCalled()
    expect(warningMock).toHaveBeenCalledTimes(1)
    const text = String(warningMock.mock.calls[0]![0])
    expect(text).toContain('1 generated')
    expect(text).toContain("Account '5200' is inactive.")
  })

  it('keeps the due sweep a success when nothing failed', async () => {
    runDue.mockResolvedValueOnce(sweep({ templatesDue: 2, generated: 2 }))
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    await (wrapper.vm as unknown as RecurringVm).runDue()
    await flushPromises()

    expect(warningMock).not.toHaveBeenCalled()
    expect(successMock).toHaveBeenCalledTimes(1)
  })
})
