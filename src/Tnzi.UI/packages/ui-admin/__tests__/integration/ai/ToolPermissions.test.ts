import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * ToolPermissions integration test — three tabs (persisted / session /
 * evaluate) over the permission bridge. The bridge is mocked:
 *   • getRules        → { hasRules, sessionRules[] }   (KPI + session tab)
 *   • getPersistedRules → 3 rows out of priority/scope order             (persisted tab)
 *   • evaluate        → a deny decision                 (evaluate tab)
 *   • create / delete → spies
 *
 * Asserts: three tabs render, KPIs reflect the snapshot, the persisted rules
 * are re-ordered by (priority desc, scope weight desc) for the visualization,
 * the badge/weight helpers map enums correctly, and running an evaluation
 * surfaces the colour-coded decision + decision-chain explainer.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const persisted = [
  // Intentionally out of resolution order: a low-priority Session rule, a
  // high-priority System rule, and a mid-priority User rule. The page must
  // re-sort to Priority desc → Scope weight desc (Session=4 > User=3 >
  // Project=2 > System=1).
  {
    id: 'r-session', toolPattern: 'shell:*', behavior: 1, scope: 3, priority: 10,
    isDestructiveOnly: false, isSubAgentOnly: false, isEnabled: true,
    creationTime: '2026-05-01T00:00:00Z',
  },
  {
    id: 'r-system', toolPattern: 'write_file', behavior: 2, scope: 0, priority: 200,
    isDestructiveOnly: true, isSubAgentOnly: false, isEnabled: true,
    creationTime: '2026-05-02T00:00:00Z',
  },
  {
    id: 'r-user', toolPattern: 'read_file', behavior: 0, scope: 2, priority: 50,
    isDestructiveOnly: false, isSubAgentOnly: false, isEnabled: false,
    creationTime: '2026-05-03T00:00:00Z',
  },
]

const snapshot = {
  hasRules: true,
  sessionRules: [
    { toolPattern: 'shell:rm', toolGroup: 'shell', behavior: 2, scope: 3, priority: 999, isSubAgentOnly: false, isWorkflowOnly: false, isDestructiveOnly: false, reason: 'block rm' },
  ],
}

const evalResult = {
  toolName: 'write_file',
  behavior: 2, // Deny
  scope: 0, // System
  matchedRulePattern: 'write_file',
  matchedToolGroup: null,
  matchedServerName: null,
  reason: 'Destructive write blocked by System policy',
}

const getRules = vi.fn(async () => snapshot)
const getPersistedRules = vi.fn(async () => persisted)
const evaluate = vi.fn(async () => evalResult)
const createPersistedRule = vi.fn(async (d: unknown) => ({ id: 'r-new', ...(d as object) }))
const updatePersistedRule = vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) }))
const deletePersistedRule = vi.fn(async () => undefined)

vi.mock('../../../src/services/bridges/permission-bridge', () => ({
  createPermissionBridge: () => ({
    getRules,
    getPersistedRules,
    evaluate,
    createPersistedRule,
    updatePersistedRule,
    deletePersistedRule,
  }),
  // Page imports these as types only, but re-export numeric enums for safety.
  PermissionBehavior: { Allow: 0, Ask: 1, Deny: 2 },
  ToolPermissionScope: { System: 0, Project: 1, User: 2, Session: 3 },
}))

import ToolPermissions from '../../../src/pages/ai/permissions/ToolPermissions.vue'

const stubs = {
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  // TKpiCard animates numbers via NNumberAnimation (tween from 0 over ~2s);
  // stub it to render the target value synchronously so text assertions hold.
  NumberAnimation: { name: 'NumberAnimation', props: ['from', 'to', 'precision'], template: '<span>{{ to }}</span>' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Tabs: { name: 'Tabs', template: '<div class="n-tabs-stub"><slot /></div>' },
  TabPane: { name: 'TabPane', props: ['name', 'tab'], template: '<div class="n-tab-pane-stub"><slot /></div>' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  InputNumber: { name: 'InputNumber', props: ['value'], template: '<input class="n-input-number-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Select: { name: 'Select', props: ['value', 'options'], template: '<div class="n-select-stub" />' },
  Switch: { name: 'Switch', props: ['value'], template: '<div class="n-switch-stub" />' },
  Statistic: {
    name: 'Statistic',
    props: ['label', 'value'],
    template: '<div class="n-statistic-stub"><span class="stat-label">{{ label }}</span><span class="stat-value">{{ value }}</span><slot /></div>',
  },
  Card: { name: 'Card', props: ['title'], template: '<div class="n-card-stub"><slot /><slot name="footer" /></div>' },
  Tag: { name: 'Tag', props: ['type'], template: '<span class="n-tag-stub" :data-type="type"><slot /></span>' },
  Tooltip: { name: 'Tooltip', template: '<span><slot name="trigger" /><slot /></span>' },
  Popconfirm: { name: 'Popconfirm', template: '<span><slot name="trigger" /><slot /></span>' },
  Divider: { name: 'Divider', template: '<hr />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', props: ['label'], template: '<div class="form-item"><slot /></div>' },
  Space: { name: 'Space', template: '<div><slot /></div>' },
}

type Vm = {
  // Persisted rules now live in the TCrudPage state (crud.items); create/delete
  // go through crud.submit / crud.handleDelete instead of bespoke methods.
  crud: {
    items: { value: typeof persisted }
    total: { value: number }
    openCreate: () => void
    openEdit: (row: Record<string, unknown>) => void
    submit: () => Promise<void>
    handleDelete: (ids: string[]) => Promise<void>
    formModal: { formData: { value: Record<string, unknown> | null } }
  }
  persistedCount: number
  sessionRules: unknown[]
  rulesSnapshot: typeof snapshot | null
  evalResult: typeof evalResult | null
  evalCtx: { toolName: string }
  scopeWeight: (s: number) => number
  behaviorWeight: (b: number) => number
  behaviorTone: (b: number) => string
  behaviorIcon: (b: number) => string
  runEvaluate: () => Promise<void>
}

describe('ToolPermissions page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getRules.mockClear()
    getPersistedRules.mockClear()
    evaluate.mockClear()
    createPersistedRule.mockClear()
    updatePersistedRule.mockClear()
    deletePersistedRule.mockClear()
  })

  it('fetches the snapshot + persisted rules on mount', async () => {
    mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    expect(getRules).toHaveBeenCalledTimes(1)
    expect(getPersistedRules).toHaveBeenCalledTimes(1)
  })

  it('renders all three tabs (persisted / session / evaluate)', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const panes = wrapper.findAll('.n-tab-pane-stub')
    // 3 tab panes mounted.
    expect(panes.length).toBe(3)
  })

  it('KPIs reflect the snapshot (persisted count, session count, active flag)', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.persistedCount).toBe(3)
    expect(vm.sessionRules).toHaveLength(1)
    expect(vm.rulesSnapshot?.hasRules).toBe(true)
    // KPI values are surfaced through NStatistic stubs.
    expect(wrapper.html()).toContain('3') // persisted count
  })

  it('persisted rules are visualized in resolution order: priority desc, then scope weight desc', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    // The CRUD fetch sorts to Priority desc → Scope weight desc before storing.
    const order = vm.crud.items.value.map((r) => r.id)
    // system (priority 200) → user (50) → session (10).
    expect(order).toEqual(['r-system', 'r-user', 'r-session'])
    // Source array (the bridge mock) is NOT mutated by the sort (fetch spreads).
    expect(persisted.map((r) => r.id)).toEqual(['r-session', 'r-system', 'r-user'])
  })

  it('scope/behavior weight helpers mirror the conflict-resolution model', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    // Scope weights: Session=4 > User=3 > Project=2 > System=1.
    expect(vm.scopeWeight(3)).toBe(4)
    expect(vm.scopeWeight(2)).toBe(3)
    expect(vm.scopeWeight(1)).toBe(2)
    expect(vm.scopeWeight(0)).toBe(1)
    // Behavior weights: Deny=2 > Ask=1 > Allow=0.
    expect(vm.behaviorWeight(2)).toBe(2)
    expect(vm.behaviorWeight(1)).toBe(1)
    expect(vm.behaviorWeight(0)).toBe(0)
  })

  it('behavior tone + icon map allow/ask/deny to success/warning/error', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.behaviorTone(0)).toBe('success')
    expect(vm.behaviorTone(1)).toBe('warning')
    expect(vm.behaviorTone(2)).toBe('error')
    expect(vm.behaviorIcon(0)).toBe('mdi:check-circle')
    expect(vm.behaviorIcon(2)).toBe('mdi:close-circle')
  })

  it('running an evaluation calls the bridge and surfaces the colour-coded decision', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    vm.evalCtx.toolName = 'write_file'
    await vm.runEvaluate()
    await flushPromises()
    expect(evaluate).toHaveBeenCalledTimes(1)
    expect(vm.evalResult?.behavior).toBe(2) // Deny
    // The big decision banner + reason are rendered.
    const html = wrapper.html()
    expect(html).toContain('t-perm-page__decision--error')
    expect(html).toContain('write_file')
    expect(wrapper.text()).toContain('Destructive write blocked by System policy')
  })

  it('the evaluate result includes a decision-chain explainer', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    vm.evalCtx.toolName = 'write_file'
    await vm.runEvaluate()
    await flushPromises()
    expect(wrapper.find('.t-perm-page__chain').exists()).toBe(true)
    // Three resolution stages are listed.
    expect(wrapper.findAll('.t-perm-page__chain li').length).toBe(3)
  })

  it('creating a rule calls createPersistedRule then refreshes', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    // Create now flows through the TCrudPage state: open → fill model → submit.
    vm.crud.openCreate()
    await flushPromises()
    ;(vm.crud.formModal.formData.value as Record<string, unknown>).toolPattern = 'delete_file'
    await vm.crud.submit()
    await flushPromises()
    expect(createPersistedRule).toHaveBeenCalledTimes(1)
    // refresh re-pulls the persisted rules (1 on mount + 1 after create).
    expect(getPersistedRules).toHaveBeenCalledTimes(2)
  })

  it('rejects a blank tool pattern before calling createPersistedRule', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    vm.crud.openCreate()
    await flushPromises()
    // Leave toolPattern blank — the page guards (schema-form `required` is
    // visual only) so a blank-pattern rule never reaches the backend.
    await expect(vm.crud.submit()).rejects.toThrow()
    await flushPromises()
    expect(createPersistedRule).not.toHaveBeenCalled()
  })

  it('editing a rule calls updatePersistedRule then refreshes', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    // Edit flows through the declarative row action → crud.openEdit → submit.
    vm.crud.openEdit({ ...persisted[1] }) // r-system
    await flushPromises()
    ;(vm.crud.formModal.formData.value as Record<string, unknown>).priority = 250
    await vm.crud.submit()
    await flushPromises()
    expect(updatePersistedRule).toHaveBeenCalledTimes(1)
    expect(updatePersistedRule.mock.calls[0][0]).toBe('r-system')
    // refresh re-pulls the persisted rules (1 on mount + 1 after update).
    expect(getPersistedRules).toHaveBeenCalledTimes(2)
  })

  it('deleting a rule calls deletePersistedRule then refreshes', async () => {
    const wrapper = mount(ToolPermissions, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    // Delete goes through the declarative row action → crud.handleDelete.
    await vm.crud.handleDelete(['r-system'])
    await flushPromises()
    expect(deletePersistedRule).toHaveBeenCalledWith('r-system')
    expect(getPersistedRules).toHaveBeenCalledTimes(2)
  })
})
