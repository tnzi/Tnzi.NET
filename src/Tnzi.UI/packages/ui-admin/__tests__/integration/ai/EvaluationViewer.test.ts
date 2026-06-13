import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const runBatchSpy = vi.fn(async () => ({
  results: [
    {
      id: 'eval-b1', agentId: 'agent-001', status: 'Completed',
      caseCount: 3, passedCount: 3, averageScore: 0.95, duration: '00:00:05',
      creationTime: '2026-04-15T00:00:00Z', resultsJson: '{}',
    },
    {
      id: 'eval-b2', agentId: 'agent-002', status: 'Completed',
      caseCount: 3, passedCount: 2, averageScore: 0.7, duration: '00:00:06',
      creationTime: '2026-04-15T00:00:00Z', resultsJson: '{}',
    },
  ],
  totalDuration: '00:00:11',
}))
const getTrendSpy = vi.fn(async (agentId: string) => ({
  agentId,
  points: [
    { runId: 'eval-1', date: '2026-04-10T00:00:00Z', score: 0.8, passRate: 0.9 },
    { runId: 'eval-2', date: '2026-04-11T00:00:00Z', score: 0.92, passRate: 1.0 },
  ],
}))
const compareVersionsSpy = vi.fn(async (agentId: string, vA: number, vB: number) => ({
  agentId,
  versionA: { versionNumber: vA, runCount: 4, averageScore: 0.82, averagePassRate: 0.85, totalCases: 40, totalPassed: 34 },
  versionB: { versionNumber: vB, runCount: 5, averageScore: 0.9, averagePassRate: 0.93, totalCases: 50, totalPassed: 46 },
  scoreDelta: 0.08,
  winner: vB,
}))
const agentsFetchSpy = vi.fn(async () => ({
  items: [
    { id: 'agent-001', name: 'Support Bot', provider: 'openai', isEnabled: true },
    { id: 'agent-002', name: 'Sales Bot', provider: 'openai', isEnabled: true },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 100,
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agents: {
      fetch: agentsFetchSpy,
    },
    evaluations: {
      fetch: vi.fn(async () => ({
        items: [
          {
            id: 'eval-1',
            agentId: 'agent-001',
            caseCount: 10,
            passedCount: 9,
            averageScore: 0.92,
            status: 'Completed',
            duration: '00:00:42',
            creationTime: '2026-04-10T00:00:00Z',
          },
          {
            id: 'eval-2',
            agentId: 'agent-002',
            caseCount: 5,
            passedCount: 5,
            averageScore: 1.0,
            status: 'Completed',
            duration: '00:00:18',
            creationTime: '2026-04-11T00:00:00Z',
          },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 50,
      })),
      create: vi.fn(async (data: unknown) => ({
        id: 'eval-3',
        ...(data as object),
        caseCount: 1,
        passedCount: 1,
        averageScore: 1.0,
        status: 'Completed',
        duration: '00:00:01',
        creationTime: '2026-04-12T00:00:00Z',
        resultsJson: '{}',
      })),
      delete: vi.fn(async () => undefined),
      getDetail: vi.fn(async (id: string) => ({
        id, agentId: 'a1', status: 'Completed', caseCount: 5, passedCount: 4,
        averageScore: 0.8, duration: '2s', creationTime: '2026-04-14T00:00:00Z',
        resultsJson: '{"cases":[]}',
      })),
      runBatch: runBatchSpy,
      getTrend: getTrendSpy,
      compareVersions: compareVersionsSpy,
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), back: vi.fn() }),
}))

import EvaluationViewer from '../../../src/pages/ai/evaluations/EvaluationViewer.vue'

describe('EvaluationViewer page (Tier 3: diff + score)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    runBatchSpy.mockClear()
    getTrendSpy.mockClear()
    compareVersionsSpy.mockClear()
    agentsFetchSpy.mockClear()
  })

  it('mounts with TContentPage chrome and loads runs on mount', async () => {
    const wrapper = mount(EvaluationViewer)
    await flushPromises()
    expect(wrapper.find('.t-content-page').exists()).toBe(true)
    const items = wrapper.findAll('.t-eval-page__run-item')
    expect(items.length).toBe(2)
  })

  it('shows a select-prompt before any run is picked', async () => {
    const wrapper = mount(EvaluationViewer)
    await flushPromises()
    expect(wrapper.find('.t-eval-page__placeholder').exists()).toBe(true)
  })

  it('exposes Run Batch / Trend / Compare toolbar entry points', async () => {
    const wrapper = mount(EvaluationViewer)
    await flushPromises()
    const labels = wrapper.findAll('button').map((b) => b.text())
    // Humanise fallback yields capitalised labels when locale keys are absent.
    expect(labels.some((l) => /batch/i.test(l))).toBe(true)
    expect(labels.some((l) => /trend/i.test(l))).toBe(true)
    expect(labels.some((l) => /compare/i.test(l))).toBe(true)
  })

  it('runs a batch evaluation through the bridge', async () => {
    const wrapper = mount(EvaluationViewer)
    await flushPromises()
    // Call the batch runner directly through the component instance so we
    // don't depend on NDrawer teleport rendering inside jsdom.
    const vm = wrapper.vm as unknown as {
      openBatchDrawer: () => void
      batchTargets: Array<{ agentId: string; versionNumber: number | null }>
      batchCases: Array<{ input: string; expectedOutput: string }>
      runBatchEval: () => Promise<void>
    }
    vm.openBatchDrawer()
    await flushPromises()
    // agents.fetch should be lazily loaded when the batch drawer opens.
    expect(agentsFetchSpy).toHaveBeenCalled()
    vm.batchTargets[0]!.agentId = 'agent-001'
    vm.batchCases[0]!.input = 'hello?'
    await vm.runBatchEval()
    expect(runBatchSpy).toHaveBeenCalledTimes(1)
    const arg = runBatchSpy.mock.calls[0]![0] as {
      targets: Array<{ agentId: string }>
      cases: Array<{ input: string }>
    }
    expect(arg.targets[0]!.agentId).toBe('agent-001')
    expect(arg.cases[0]!.input).toBe('hello?')
  })

  it('loads a score trend for the selected agent', async () => {
    const wrapper = mount(EvaluationViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      trendAgentId: string
      loadTrend: () => Promise<void>
    }
    vm.trendAgentId = 'agent-001'
    await vm.loadTrend()
    expect(getTrendSpy).toHaveBeenCalledWith('agent-001', 20)
  })

  it('compares two agent versions through the bridge', async () => {
    const wrapper = mount(EvaluationViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      compareAgentId: string
      compareVersionA: number | null
      compareVersionB: number | null
      loadComparison: () => Promise<void>
    }
    vm.compareAgentId = 'agent-001'
    vm.compareVersionA = 1
    vm.compareVersionB = 2
    await vm.loadComparison()
    expect(compareVersionsSpy).toHaveBeenCalledWith('agent-001', 1, 2)
  })
})
