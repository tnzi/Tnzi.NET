import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    // Phase J: WorkflowRunViewer now pulls the workflow list for the filter
    // dropdown on mount — mock the workflows surface as well.
    workflows: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'wf-001', name: 'Sample Workflow', steps: [], executionMode: 'Sequential', isEnabled: true, creationTime: '2026-04-10T00:00:00Z' },
        ],
        totalCount: 1,
        pageIndex: 1,
        pageSize: 20,
      })),
    },
    workflowRuns: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'run1', executionId: 'exec-1', workflowDefinitionId: 'wf-001', status: 'Completed', completedStepCount: 5, awaitingApprovalCount: 0, creationTime: '2026-04-10T00:00:00Z', completedTime: '2026-04-10T00:01:00Z', updatedTime: '2026-04-10T00:01:00Z' },
          { id: 'run2', executionId: 'exec-2', workflowDefinitionId: 'wf-002', status: 'Pending', completedStepCount: 2, awaitingApprovalCount: 1, creationTime: '2026-04-11T00:00:00Z', completedTime: null, updatedTime: '2026-04-11T00:02:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      tail: vi.fn(() => Promise.reject(new Error('not implemented'))),
      getDetail: vi.fn(async (id: string) => ({
        id, executionId: 'exec-' + id, workflowDefinitionId: 'wf-001', status: 'Completed',
        completedStepCount: 1, awaitingApprovalCount: 0,
        creationTime: '2026-04-10T00:00:00Z', completedTime: '2026-04-10T00:01:00Z', updatedTime: '2026-04-10T00:01:00Z',
        initialInput: '{}', completedStepIds: ['s1'], stepsAwaitingApproval: [], stepOutputs: { s1: 'output' },
      })),
      resume: vi.fn(),
      approveStep: vi.fn(),
      rejectStep: vi.fn(),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), back: vi.fn() }),
}))

import WorkflowRunViewer from '../../../src/pages/ai/workflows/WorkflowRunViewer.vue'

describe('WorkflowRunViewer page (Tier 3: timeline)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('mounts the master-detail layout and renders run rows', async () => {
    const wrapper = mount(WorkflowRunViewer)
    await flushPromises()
    expect(wrapper.find('.t-master-detail').exists()).toBe(true)
    const items = wrapper.findAll('.t-wf-run-page__run-item')
    expect(items.length).toBe(2)
  })

  it('shows a placeholder before a run is picked', async () => {
    const wrapper = mount(WorkflowRunViewer)
    await flushPromises()
    expect(wrapper.find('.t-wf-run-page__placeholder').exists()).toBe(true)
  })
})
