import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
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
})
