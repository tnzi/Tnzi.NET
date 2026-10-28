import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * The run list pages with "Load more". Three ways it used to lose rows:
 *  - a failed Load more kept the page counter it had already advanced, so the
 *    retry asked for the page after the failed one and those runs never showed;
 *  - an answer for an older query (Load more, then a filter change) landed
 *    after the newer one and was appended under it;
 *  - a run action re-read the list at the advanced page index, replacing the
 *    whole list with that page alone.
 */
const runsFetch = vi.hoisted(() => vi.fn())

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    workflows: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    },
    workflowRuns: {
      fetch: runsFetch,
      getDetail: vi.fn(async (id: string) => ({
        id, executionId: 'exec-' + id, workflowDefinitionId: 'wf-001', status: 'Paused',
        completedStepCount: 0, awaitingApprovalCount: 0,
        creationTime: '2026-04-10T00:00:00Z', completedTime: null, updatedTime: '2026-04-10T00:00:00Z',
        initialInput: '{}', completedStepIds: [], stepsAwaitingApproval: [], stepOutputs: {},
      })),
      getSignals: vi.fn(async () => []),
      resume: vi.fn(async () => undefined),
      approveStep: vi.fn(),
      rejectStep: vi.fn(),
      cancel: vi.fn(async () => undefined),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), back: vi.fn() }),
}))

import WorkflowRunViewer from '../../../src/pages/ai/workflows/WorkflowRunViewer.vue'

function run(id: string) {
  return {
    id, executionId: 'exec-' + id, workflowDefinitionId: 'wf-001', status: 'Completed',
    completedStepCount: 1, awaitingApprovalCount: 0,
    creationTime: '2026-04-10T00:00:00Z', completedTime: null, updatedTime: '2026-04-10T00:00:00Z',
  }
}

function page(pageIndex: number, total = 45) {
  const start = (pageIndex - 1) * 20
  const count = Math.max(0, Math.min(20, total - start))
  return {
    items: Array.from({ length: count }, (_, i) => run(`r${start + i + 1}`)),
    totalCount: total,
    pageIndex,
    pageSize: 20,
  }
}

function deferred<T>() {
  let resolve!: (v: T) => void
  let reject!: (e: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

type Vm = {
  runs: Array<{ id: string }>
  loadMore: () => Promise<void>
  onFilterChange: () => void
  selectRun: (id: string) => Promise<void>
  afterAction: () => Promise<void>
}

function requestedPages(): number[] {
  return runsFetch.mock.calls.map((c) => (c[0] as { pageIndex: number }).pageIndex)
}

describe('WorkflowRunViewer run list paging', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    runsFetch.mockReset()
  })

  it('asks for the failed page again when Load more is retried', async () => {
    runsFetch.mockImplementationOnce(async () => page(1))
    const wrapper = mount(WorkflowRunViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.runs).toHaveLength(20)

    runsFetch.mockImplementationOnce(async () => {
      throw new Error('Gateway timeout')
    })
    await vm.loadMore()
    await flushPromises()

    runsFetch.mockImplementationOnce(async () => page(2))
    await vm.loadMore()
    await flushPromises()

    expect(requestedPages()).toEqual([1, 2, 2])
    expect(vm.runs.map((r) => r.id)).toEqual(page(1).items.concat(page(2).items).map((r) => r.id))
  })

  it('does not append an older query page under a newer list', async () => {
    runsFetch.mockImplementationOnce(async () => page(1))
    const wrapper = mount(WorkflowRunViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm

    const slow = deferred<ReturnType<typeof page>>()
    runsFetch.mockImplementationOnce(() => slow.promise)
    const more = vm.loadMore()

    // The operator changes a filter while page 2 is still on its way.
    const filtered = { items: [run('f1')], totalCount: 1, pageIndex: 1, pageSize: 20 }
    runsFetch.mockImplementationOnce(async () => filtered)
    vm.onFilterChange()
    await flushPromises()

    slow.resolve(page(2))
    await more
    await flushPromises()

    expect(vm.runs.map((r) => r.id)).toEqual(['f1'])
  })

  it('a second Load more click while one is loading does not skip a page', async () => {
    runsFetch.mockImplementationOnce(async () => page(1))
    const wrapper = mount(WorkflowRunViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm

    const slow = deferred<ReturnType<typeof page>>()
    runsFetch.mockImplementationOnce(() => slow.promise)
    runsFetch.mockImplementation(async (q: { pageIndex: number }) => page(q.pageIndex))
    const first = vm.loadMore()
    const second = vm.loadMore()
    slow.resolve(page(2))
    await Promise.all([first, second])
    await flushPromises()

    expect(requestedPages()).toEqual([1, 2])
    expect(vm.runs).toHaveLength(40)
  })

  it('re-reads the list from the first page after a run action', async () => {
    runsFetch.mockImplementation(async (q: { pageIndex: number }) => page(q.pageIndex))
    const wrapper = mount(WorkflowRunViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    await vm.loadMore()
    await flushPromises()
    expect(vm.runs).toHaveLength(40)

    await vm.selectRun('r1')
    await vm.afterAction()
    await flushPromises()

    expect(requestedPages().at(-1)).toBe(1)
    expect(vm.runs[0].id).toBe('r1')
  })
})
