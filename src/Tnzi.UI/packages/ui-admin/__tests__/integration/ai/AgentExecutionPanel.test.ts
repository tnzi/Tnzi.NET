import { describe, expect, it, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AgentExecutionPanel from '../../../src/pages/ai/agents/sections/AgentExecutionPanel.vue'
import type {
  CliAgentBindingDto,
  CliRuntimeDto,
  UpsertCliAgentBindingDto,
} from '../../../src/services/bridges/cli-agent-bridge'

const runtime: CliRuntimeDto = {
  id: 'rt-1',
  hostId: 'BUILD-01',
  providerKey: 'claude',
  providerDisplayName: 'Claude Code',
  protocol: 'StreamJson',
  name: 'Claude Code @ BUILD-01',
  executablePath: 'C:/tools/claude.cmd',
  cliVersion: '2.1.0',
  mode: 'InProcess',
  status: 'Online',
  lastSeenAt: null,
  maxConcurrentRuns: 2,
  launchHeader: 'claude (stream-json)',
  creationTime: '2026-07-31T09:00:00Z',
}

const binding: CliAgentBindingDto = {
  id: 'b-1',
  agentId: 'agent-1',
  cliRuntimeId: 'rt-1',
  cliRuntimeName: runtime.name,
  providerKey: 'claude',
  model: 'sonnet',
  thinkingLevel: 'high',
  customArgs: ['--verbose'],
  mcpConfigJson: null,
  workDirectoryMode: 'Isolated',
  userWorkDirectory: null,
  injectAgentInstructions: true,
  materializeSkills: true,
  idleWatchdog: '00:05:00',
}

type PanelVm = {
  mode: 'builtIn' | 'external'
  form: Record<string, unknown>
  isDirty: boolean
  canSave: boolean
  isBound: boolean
  hasRuntimes: boolean
  idleWatchdogMinutes: number | null
  mcpConfigError: string | null
  save: () => void
  reset: () => void
}

function mountPanel(props: Partial<InstanceType<typeof AgentExecutionPanel>['$props']> = {}) {
  return mount(AgentExecutionPanel, {
    props: { binding: null, runtimes: [runtime], canEdit: true, ...props } as never,
    global: { stubs: { TSvgIcon: true } },
  })
}

beforeEach(() => setActivePinia(createPinia()))

describe('AgentExecutionPanel', () => {
  it('reads the absence of a binding as built-in execution', () => {
    // The backend has no per-agent flag: no binding row IS built-in. The panel
    // must not invent a third "unset" state.
    const vm = mountPanel().vm as unknown as PanelVm
    expect(vm.mode).toBe('builtIn')
    expect(vm.isBound).toBe(false)
    expect(vm.isDirty).toBe(false)
  })

  it('hydrates every field from an existing binding', () => {
    const vm = mountPanel({ binding }).vm as unknown as PanelVm
    expect(vm.mode).toBe('external')
    expect(vm.form.cliRuntimeId).toBe('rt-1')
    expect(vm.form.model).toBe('sonnet')
    expect(vm.form.customArgs).toEqual(['--verbose'])
    // TimeSpan on the wire, minutes in the UI.
    expect(vm.idleWatchdogMinutes).toBe(5)
    expect(vm.isDirty).toBe(false)
  })

  it('round-trips the idle watchdog between minutes and a TimeSpan', () => {
    const vm = mountPanel({ binding }).vm as unknown as PanelVm
    vm.idleWatchdogMinutes = 90
    expect(vm.form.idleWatchdog).toBe('01:30:00')
    vm.idleWatchdogMinutes = null
    expect(vm.form.idleWatchdog).toBeNull()
  })

  it('preselects the runtime when there is exactly one', async () => {
    const wrapper = mountPanel()
    const vm = wrapper.vm as unknown as PanelVm
    vm.mode = 'external'
    await wrapper.vm.$nextTick()
    expect(vm.form.cliRuntimeId).toBe('rt-1')
  })

  it('does not guess a runtime when there are several', async () => {
    const second = { ...runtime, id: 'rt-2', name: 'Kimi @ BUILD-01' }
    const wrapper = mountPanel({ runtimes: [runtime, second] })
    const vm = wrapper.vm as unknown as PanelVm
    vm.mode = 'external'
    await wrapper.vm.$nextTick()
    expect(vm.form.cliRuntimeId).toBeNull()
  })

  it('emits the binding payload on save', () => {
    const wrapper = mountPanel({ binding })
    const vm = wrapper.vm as unknown as PanelVm
    vm.form.model = 'opus'
    vm.save()

    const payload = wrapper.emitted('save')?.[0]?.[0] as UpsertCliAgentBindingDto
    expect(payload.cliRuntimeId).toBe('rt-1')
    expect(payload.model).toBe('opus')
    expect(payload.customArgs).toEqual(['--verbose'])
  })

  it('treats switching back to built-in as an unbind, not a save', () => {
    // Save with mode=builtIn must not post an empty binding - the only way back
    // to built-in execution is deleting the row.
    const wrapper = mountPanel({ binding })
    const vm = wrapper.vm as unknown as PanelVm
    vm.mode = 'builtIn'
    vm.save()

    expect(wrapper.emitted('unbind')).toHaveLength(1)
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('blocks save on malformed MCP JSON', () => {
    // The backend fails closed to "no managed MCP servers", which surfaces an
    // hour later as an agent that quietly lost its tools.
    const wrapper = mountPanel({ binding })
    const vm = wrapper.vm as unknown as PanelVm
    vm.form.mcpConfigJson = '{ not json'
    expect(vm.mcpConfigError).not.toBeNull()
    expect(vm.canSave).toBe(false)
  })

  it('blocks save on a user-provided work directory with no path', () => {
    const vm = mountPanel({ binding }).vm as unknown as PanelVm
    vm.form.workDirectoryMode = 'UserProvided'
    vm.form.userWorkDirectory = '   '
    expect(vm.canSave).toBe(false)

    vm.form.userWorkDirectory = 'D:/work/repo'
    expect(vm.canSave).toBe(true)
  })

  it('reports no runtimes rather than offering an empty dropdown', () => {
    const wrapper = mountPanel({ runtimes: [] })
    const vm = wrapper.vm as unknown as PanelVm
    expect(vm.hasRuntimes).toBe(false)
    expect(vm.canSave).toBe(false)
  })

  it('reverts to the persisted binding on discard', () => {
    const vm = mountPanel({ binding }).vm as unknown as PanelVm
    vm.form.model = 'changed'
    expect(vm.isDirty).toBe(true)
    vm.reset()
    expect(vm.form.model).toBe('sonnet')
    expect(vm.isDirty).toBe(false)
  })

  it('drops blank optional fields instead of persisting empty strings', () => {
    const wrapper = mountPanel({ binding })
    const vm = wrapper.vm as unknown as PanelVm
    vm.form.model = '  '
    vm.form.thinkingLevel = ''
    vm.save()

    const payload = wrapper.emitted('save')?.[0]?.[0] as UpsertCliAgentBindingDto
    expect(payload.model).toBeNull()
    expect(payload.thinkingLevel).toBeNull()
  })
})

describe('AgentExecutionPanel - read-only viewer', () => {
  it('offers no write affordance without ai.cliBinding.update', () => {
    // The backend [ApiAuthorize] is the real wall; hiding the buttons just keeps
    // the page from offering something that will 403.
    const editable = mountPanel({ binding, canEdit: true })
    expect(editable.find('.n-button--primary-type').exists()).toBe(true)
    expect(editable.find('.n-button--error-type').exists()).toBe(true)

    const readOnly = mountPanel({ binding, canEdit: false })
    expect(readOnly.find('.n-button--primary-type').exists()).toBe(false)
    expect(readOnly.find('.n-button--error-type').exists()).toBe(false)
    // Discard stays: reverting local edits is not a write.
    expect(readOnly.findAll('button').length).toBeGreaterThan(0)
  })
})
