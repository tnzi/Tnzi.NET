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
    // `data-node-kinds` exposes the kind the page resolved for each node so a
    // test can prove what the editor THINKS a step is (which is what it will
    // save back) without reaching into the SFC's private draft state.
    template:
      `<div data-test="canvas-stub" :data-node-count="nodes?.length ?? 0"`
      + ` :data-node-kinds="(nodes ?? []).map((n) => n.data?.kind).join(',')">canvas</div>`,
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

type MockStep = Record<string, unknown>
let mockSteps: MockStep[] = []
const defaultSteps = (): MockStep[] => [
  { stepId: 'step-a', order: 1, dependsOn: [], maxRetries: 0, retryDelaySeconds: 5, requiresApproval: false },
  { stepId: 'step-b', order: 2, dependsOn: ['step-a'], maxRetries: 0, retryDelaySeconds: 5, requiresApproval: false },
]

const mockGetById = vi.fn(async (id: string) => ({
  id,
  name: 'Hello Workflow',
  description: 'demo',
  executionMode: 'Sequential',
  isEnabled: true,
  steps: mockSteps,
  creationTime: '2026-04-10T00:00:00Z',
  lastModificationTime: '2026-04-10T00:01:00Z',
}))

const mockGetExecutionStats = vi.fn()

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    workflows: {
      getById: mockGetById,
      update: vi.fn(),
      validate: vi.fn(),
      run: vi.fn(),
      publish: vi.fn(),
      unpublish: vi.fn(),
      getExecutionStats: mockGetExecutionStats,
      getVersions: vi.fn(async () => []),
      getVersion: vi.fn(),
      restoreVersion: vi.fn(),
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
    mockSteps = defaultSteps()
    mockGetById.mockClear()
    mockGetExecutionStats.mockReset().mockResolvedValue({
      workflowId: 'wf-1',
      totalExecutions: 8,
      successRate: 0.75,
      avgDurationMs: 1500,
      minDurationMs: 400,
      maxDurationMs: 90_000,
      p95DurationMs: 80_000,
    })
  })

  it('shows the workflow execution stats in the metadata panel', async () => {
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    expect(mockGetExecutionStats).toHaveBeenCalledWith('wf-1')
    expect(wrapper.find('[data-test="wf-exec-stats-total"]').text()).toBe('8')
    expect(wrapper.find('[data-test="wf-exec-stats-success"]').text()).toBe('75.0%')
    expect(wrapper.find('[data-test="wf-exec-stats"]').text()).toContain('1.5 s')
  })

  it('shows a refused stats read as an error, not as zero runs', async () => {
    mockGetExecutionStats.mockReset().mockRejectedValue(new Error('Forbidden'))
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    expect(wrapper.find('[data-test="wf-exec-stats-error"]').text()).toContain('Forbidden')
    expect(wrapper.find('[data-test="wf-exec-stats-total"]').exists()).toBe(false)
  })

  it('reloads the definition after a version is restored', async () => {
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    expect(mockGetById).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-test="wf-open-history"]').exists()).toBe(true)
    wrapper.findComponent({ name: 'WorkflowVersionsDrawer' }).vm.$emit('restored', 1)
    await flushPromises()
    expect(mockGetById).toHaveBeenCalledTimes(2)
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

  it('resolves the node kind from configuration.nodeType, the key the backend executor reads', async () => {
    mockSteps = [
      { stepId: 'route', order: 1, dependsOn: [], configuration: { nodeType: 'router' } },
      { stepId: 'fan', order: 2, dependsOn: ['route'], configuration: { nodeType: 'parallel' } },
      { stepId: 'plain', order: 3, dependsOn: ['fan'], configuration: {} },
    ]
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    await new Promise((r) => setTimeout(r, 5))
    await flushPromises()
    expect(wrapper.find('[data-test="canvas-stub"]').attributes('data-node-kinds')).toBe('router,parallel,agent')
  })

  it('migrates a definition saved with the legacy __nodeType key so it renders (and re-saves) as its real kind', async () => {
    // Before 2026-09-12 the editor wrote `__nodeType`, which the backend never
    // read: the step looked like a router in the editor and ran as an agent.
    mockSteps = [
      { stepId: 'route', order: 1, dependsOn: [], configuration: { __nodeType: 'router', __x: '1', __y: '2' } },
    ]
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    await new Promise((r) => setTimeout(r, 5))
    await flushPromises()
    expect(wrapper.find('[data-test="canvas-stub"]').attributes('data-node-kinds')).toBe('router')

    // The JSON view mirrors draft.steps: the migrated bag carries `nodeType`
    // and no longer carries the legacy key, so the next save heals the row.
    const json = (wrapper.vm as unknown as { stepsJson: string }).stepsJson
    const saved = JSON.parse(json) as Array<{ configuration: Record<string, string> }>
    expect(saved[0].configuration.nodeType).toBe('router')
    expect(saved[0].configuration).not.toHaveProperty('__nodeType')
    expect(saved[0].configuration.__x).toBe('1')
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
