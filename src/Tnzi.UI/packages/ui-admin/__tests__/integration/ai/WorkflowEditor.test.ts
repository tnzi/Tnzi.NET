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

let mockConfiguration: Record<string, unknown> | null = null

const mockGetById = vi.fn(async (id: string) => ({
  id,
  name: 'Hello Workflow',
  description: 'demo',
  executionMode: 'Sequential',
  isEnabled: true,
  steps: mockSteps,
  configuration: mockConfiguration,
  creationTime: '2026-04-10T00:00:00Z',
  lastModificationTime: '2026-04-10T00:01:00Z',
}))

// Echoes the saved definition back, as the server does.
const mockUpdate = vi.fn(async (id: string, payload: Record<string, unknown>) => ({
  id,
  name: payload.name,
  description: payload.description,
  executionMode: payload.executionMode,
  isEnabled: payload.isEnabled,
  steps: payload.steps,
  configuration: 'configuration' in payload ? payload.configuration : mockConfiguration,
  creationTime: '2026-04-10T00:00:00Z',
}))

const mockGetExecutionStats = vi.fn()
const mockValidate = vi.fn(async () => ({ isValid: true, errors: [], warnings: [] }))
const mockRun = vi.fn(async () => ({ executionId: 'x-1' }))

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    workflows: {
      getById: mockGetById,
      update: mockUpdate,
      validate: mockValidate,
      run: mockRun,
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
    mockConfiguration = null
    mockGetById.mockClear()
    mockUpdate.mockClear()
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

/**
 * Renaming a step on every keystroke rewired the graph through each
 * intermediate value: editing `step-10` passes through `step-1`, which named an
 * existing step, so its dependents were rewritten and the selection (a find by
 * id) jumped to the real `step-1`, whose identity the next keystrokes took over.
 * The graph configuration (conditional edges, loops) was never rewritten at all.
 */
describe('WorkflowEditor - renaming and deleting steps', () => {
  const step = (stepId: string, dependsOn: string[], order: number): MockStep => ({
    stepId, order, dependsOn, maxRetries: 0, retryDelaySeconds: 5, requiresApproval: false,
  })

  type EditorVm = {
    selectStep: (id: string) => void
    deleteStep: (id: string) => void
    onSave: () => Promise<void>
    draft: { steps: Array<{ stepId: string; dependsOn: string[] | null }> }
  }

  beforeEach(() => {
    setActivePinia(createPinia())
    routeParams = { id: 'wf-1' }
    mockSteps = [
      step('step-1', [], 1),
      step('step-10', ['step-1'], 2),
      step('step-11', ['step-10'], 3),
      step('step-2', ['step-1'], 4),
    ]
    mockConfiguration = null
    mockUpdate.mockClear()
    mockGetExecutionStats.mockReset().mockResolvedValue(null)
  })

  async function mountEditor() {
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    await new Promise((r) => setTimeout(r, 5))
    await flushPromises()
    return wrapper
  }

  async function stepIdInput(wrapper: Awaited<ReturnType<typeof mountEditor>>, id: string) {
    ;(wrapper.vm as unknown as EditorVm).selectStep(id)
    await flushPromises()
    const input = wrapper.findAll('input').find((el) => (el.element as HTMLInputElement).value === id)
    if (!input) throw new Error(`no input holds ${id}`)
    return input
  }

  const ids = (vm: EditorVm) => vm.draft.steps.map((s) => s.stepId)
  const deps = (vm: EditorVm, id: string) => vm.draft.steps.find((s) => s.stepId === id)?.dependsOn

  it('★ typing through an id that names another step does not hijack it', async () => {
    const wrapper = await mountEditor()
    const vm = wrapper.vm as unknown as EditorVm
    const input = await stepIdInput(wrapper, 'step-10')

    await input.setValue('step-1') // a keystroke on the way to "step-1b"
    await input.setValue('step-1b')
    await input.trigger('blur')

    expect(ids(vm)).toEqual(['step-1', 'step-1b', 'step-11', 'step-2'])
    expect(deps(vm, 'step-1b')).toEqual(['step-1'])
    expect(deps(vm, 'step-11')).toEqual(['step-1b'])
    expect(deps(vm, 'step-2')).toEqual(['step-1'])
  })

  it('★ refuses an id another step already has, ignoring case like the backend', async () => {
    const wrapper = await mountEditor()
    const vm = wrapper.vm as unknown as EditorVm
    const input = await stepIdInput(wrapper, 'step-10')

    await input.setValue('STEP-1')
    await input.trigger('blur')
    await flushPromises()

    expect(ids(vm)).toEqual(['step-1', 'step-10', 'step-11', 'step-2'])
    expect(deps(vm, 'step-11')).toEqual(['step-10'])
    expect((input.element as HTMLInputElement).value).toBe('step-10')
  })

  it('★ a rename rewrites the conditional edges and loops that name the step', async () => {
    mockConfiguration = {
      conditionalEdges: [{ fromNodeId: 'step-10', routes: { yes: 'step-11', no: 'step-2' }, defaultTarget: 'step-10' }],
      loops: { refine: { nodeIds: ['step-10', 'step-11'], maxIterations: 3 } },
    }
    const wrapper = await mountEditor()
    const vm = wrapper.vm as unknown as EditorVm
    const input = await stepIdInput(wrapper, 'step-10')
    await input.setValue('review')
    await input.trigger('keydown', { key: 'Enter' })
    await vm.onSave()

    const payload = mockUpdate.mock.calls[0]![1] as Record<string, unknown>
    expect(payload.configuration).toEqual({
      conditionalEdges: [{ fromNodeId: 'review', routes: { yes: 'step-11', no: 'step-2' }, defaultTarget: 'review' }],
      loops: { refine: { nodeIds: ['review', 'step-11'], maxIterations: 3 } },
    })
  })

  it('★ deleting a step drops it from the conditional edges and loops', async () => {
    mockConfiguration = {
      conditionalEdges: [
        { fromNodeId: 'step-10', routes: { yes: 'step-11', no: 'step-2' }, defaultTarget: 'step-11' },
        { fromNodeId: 'step-11', routes: { done: 'step-2' } },
      ],
      loops: { refine: { nodeIds: ['step-10', 'step-11'], maxIterations: 3 }, solo: { nodeIds: ['step-11'], maxIterations: 2 } },
    }
    const wrapper = await mountEditor()
    const vm = wrapper.vm as unknown as EditorVm
    vm.deleteStep('step-11')
    await vm.onSave()

    const payload = mockUpdate.mock.calls[0]![1] as Record<string, unknown>
    expect(payload.configuration).toEqual({
      conditionalEdges: [{ fromNodeId: 'step-10', routes: { no: 'step-2' }, defaultTarget: null }],
      loops: { refine: { nodeIds: ['step-10'], maxIterations: 3 } },
    })
  })

  it('leaves the stored configuration alone when nothing referenced by it changed', async () => {
    mockConfiguration = {
      conditionalEdges: [{ fromNodeId: 'step-10', routes: { yes: 'step-11' } }],
    }
    const wrapper = await mountEditor()
    const vm = wrapper.vm as unknown as EditorVm
    const input = await stepIdInput(wrapper, 'step-2')
    await input.setValue('summary')
    await input.trigger('blur')
    await vm.onSave()

    const payload = mockUpdate.mock.calls[0]![1] as Record<string, unknown>
    expect('configuration' in payload).toBe(false)
    expect(ids(vm)).toContain('summary')
  })
})

// An emptied description went out as `null`, which the update reads as "no
// change": "Saved" showed and the old description stayed. An empty string clears.
describe('WorkflowEditor - clearing the description', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    routeParams = { id: 'wf-1' }
    mockSteps = defaultSteps()
    mockConfiguration = null
    mockUpdate.mockClear()
    mockGetExecutionStats.mockReset().mockResolvedValue(null)
  })

  // Validate and Run act on the SAVED definition (the server reads it by id).
  // With unsaved edits on the canvas, "Validation passed" and "Run" read as
  // statements about what the author sees, which is not what was checked or run.
  it('Validate and Run wait for unsaved edits to be saved', async () => {
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      onStepsJsonChange: (v: string) => void
      onValidate: () => Promise<void>
      confirmRun: () => Promise<void>
    }
    const validateBtn = () => wrapper.find('[data-test="wf-validate"]')
    const runBtn = () => wrapper.find('[data-test="wf-run"]')
    expect(validateBtn().attributes('disabled')).toBeUndefined()
    expect(runBtn().attributes('disabled')).toBeUndefined()

    vm.onStepsJsonChange(JSON.stringify([{ stepId: 'step-a', order: 1, dependsOn: ['step-a'] }]))
    await flushPromises()
    expect(validateBtn().attributes('disabled')).toBeDefined()
    expect(runBtn().attributes('disabled')).toBeDefined()

    // Reached another way (a stale click), neither goes to the server either.
    await vm.onValidate()
    await vm.confirmRun()
    expect(mockValidate).not.toHaveBeenCalled()
    expect(mockRun).not.toHaveBeenCalled()
  })

  it('sends an empty description as an empty string', async () => {
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    const vm = wrapper.vm as unknown as { draft: { description: string }; onSave: () => Promise<void> }

    vm.draft.description = ''
    await vm.onSave()

    expect(mockUpdate).toHaveBeenCalledTimes(1)
    expect((mockUpdate.mock.calls[0]![1] as { description: unknown }).description).toBe('')
  })
})
