import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase J - WorkflowEditor full editor test.
 *
 * The page now fetches the workflow definition on mount via
 * `bridge.workflows.getById`, hydrates the metadata form, renders a vue-flow
 * canvas (lazy-loaded from @tnzi/ui-ai) plus a JSON steps editor. The mocks
 * cover both shells.
 */

vi.mock('@tnzi/ui-ai/workflow', () => ({
  TWorkflowCanvas: {
    name: 'WorkflowCanvas',
    props: ['nodes', 'edges'],
    template: '<div data-test="canvas-stub" :data-node-count="nodes?.length ?? 0">canvas</div>',
  },
}))

vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('naive-ui')
  return {
    ...actual,
    useMessage: () => ({
      success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
    }),
  }
})

let routeParams: Record<string, unknown> = { id: 'wf-1' }
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams }),
  // `resolve` is part of the real router contract - the page uses it to build
  // its back-target fallback by route NAME.
  useRouter: () => ({
    push: vi.fn(() => Promise.resolve()),
    resolve: (to: { name: string }) => ({ path: `/admin/${String(to.name).replace(/\./g, '/')}` }),
  }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const mockGetById = vi.fn(async (id: string) => ({
  id,
  name: 'Hello Workflow',
  description: 'demo',
  executionMode: 'Sequential',
  isEnabled: true,
  steps: [
    { stepId: 'step-a', order: 1, dependsOn: [], maxRetries: 0, retryDelaySeconds: 5, requiresApproval: false },
    { stepId: 'step-b', order: 2, dependsOn: ['step-a'], maxRetries: 0, retryDelaySeconds: 5, requiresApproval: false },
  ],
  creationTime: '2026-04-10T00:00:00Z',
  lastModificationTime: '2026-04-10T00:01:00Z',
}))

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    workflows: {
      getById: mockGetById,
      update: vi.fn(),
      validate: vi.fn(),
      run: vi.fn(),
      publish: vi.fn(),
      unpublish: vi.fn(),
    },
  }),
  // 0.2.72+ (B4): the bridge now re-exports `WorkflowExecutionMode`
  // so pages can consume the enum value without reaching into
  // `@tnzi/core/services/ai`. The mock must mirror that re-export -
  // string enums (member name === value) matching the backend
  // JsonStringEnumConverter PascalCase serialization.
  WorkflowExecutionMode: {
    Sequential: 'Sequential',
    Parallel: 'Parallel',
    Dag: 'Dag',
  },
}))

import WorkflowEditor from '../../../src/pages/ai/workflows/WorkflowEditor.vue'

describe('WorkflowEditor page (Phase J full editor)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    routeParams = { id: 'wf-1' }
    mockGetById.mockClear()
  })

  it('fetches workflow on mount and renders the canvas with derived nodes', async () => {
    const wrapper = mount(WorkflowEditor)
    // Two await rounds: one for getById, one for Suspense canvas load.
    await flushPromises()
    await new Promise((r) => setTimeout(r, 5))
    await flushPromises()
    expect(mockGetById).toHaveBeenCalledWith('wf-1')
    const stub = wrapper.find('[data-test="canvas-stub"]')
    expect(stub.exists()).toBe(true)
    expect(stub.attributes('data-node-count')).toBe('2')
  })

  it('renders empty state when route has no id', async () => {
    routeParams = {}
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    expect(wrapper.find('[data-test="canvas-stub"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Select a workflow to edit.')
    expect(mockGetById).not.toHaveBeenCalled()
  })
})
