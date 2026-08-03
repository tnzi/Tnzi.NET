import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const runtimes = [
  {
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
    lastSeenAt: '2026-07-31T10:00:00Z',
    maxConcurrentRuns: 2,
    launchHeader: 'claude (stream-json)',
    creationTime: '2026-07-31T09:00:00Z',
  },
]

const providers = [
  { key: 'claude', displayName: 'Claude Code', protocol: 'StreamJson', defaultExecutable: 'claude', enabled: true, implemented: true },
  // Present in the catalogue but unrunnable in this backend version - the page
  // must say so rather than silently listing it as a choice.
  { key: 'codex', displayName: 'Codex', protocol: 'VendorAppServer', defaultExecutable: 'codex', enabled: true, implemented: false },
]

const runs = [
  {
    id: 'run-1',
    agentId: 'agent-1',
    cliRuntimeId: 'rt-1',
    providerKey: 'claude',
    status: 'Running',
    priority: 0,
    prompt: 'Fix the flaky test',
    durationMs: 0,
    creationTime: '2026-07-31T10:05:00Z',
  },
  {
    id: 'run-2',
    agentId: 'agent-1',
    cliRuntimeId: 'rt-1',
    providerKey: 'claude',
    status: 'Completed',
    priority: 0,
    prompt: 'Summarise the audit log',
    output: 'Done.',
    durationMs: 6400,
    estimatedCostUsd: 0.1147,
    creationTime: '2026-07-31T09:30:00Z',
  },
]

const bridge = {
  runtimes: {
    list: vi.fn(async () => runtimes),
    providers: vi.fn(async () => providers),
    probe: vi.fn(async () => ({ runtimes, notFound: ['qwen'] })),
    update: vi.fn(async () => runtimes[0]),
    remove: vi.fn(async () => undefined),
  },
  bindings: {
    get: vi.fn(async () => null),
    upsert: vi.fn(),
    remove: vi.fn(),
  },
  runs: {
    list: vi.fn(async () => ({
      items: runs,
      totalCount: runs.length,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    get: vi.fn(async () => runs[1]),
    messages: vi.fn(async () => [
      { id: 'm1', runId: 'run-2', sequence: 1, type: 'Status', status: 'running', creationTime: '2026-07-31T09:30:01Z' },
      { id: 'm2', runId: 'run-2', sequence: 2, type: 'Text', content: 'Done.', creationTime: '2026-07-31T09:30:05Z' },
    ]),
    cancel: vi.fn(async () => undefined),
    streamUrl: vi.fn(() => '/admin/ai/cli-runs/run-2/stream?fromSequence=0'),
  },
}

vi.mock('../../../src/services/bridges/cli-agent-bridge', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../src/services/bridges/cli-agent-bridge')>()
  return {
    ...original,
    createCliAgentBridge: () => bridge,
  }
})

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({}),
}))

const stubs = {
  TSvgIcon: true,
  TRelativeTime: true,
  TDescriptions: true,
  TEmpty: true,
  NAlert: { template: '<div class="stub-alert"><slot /></div>' },
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

describe('CliRuntimes', () => {
  it('lists probed runtimes and loads the provider catalogue', async () => {
    const CliRuntimes = (await import('../../../src/pages/ai/cli/CliRuntimes.vue')).default
    const wrapper = mount(CliRuntimes, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runtimes.list).toHaveBeenCalled())
    await vi.waitFor(() => expect(bridge.runtimes.providers).toHaveBeenCalled())
    expect(wrapper.exists()).toBe(true)
  })

  it('surfaces enabled providers whose protocol has no adapter', async () => {
    // Hiding them would leave an admin asking why the CLI they installed never
    // shows up; the page names them instead.
    const CliRuntimes = (await import('../../../src/pages/ai/cli/CliRuntimes.vue')).default
    const wrapper = mount(CliRuntimes, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runtimes.providers).toHaveBeenCalled())
    await wrapper.vm.$nextTick()
    expect(wrapper.html()).toContain('Codex')
  })

  it('probes on demand and refreshes the list', async () => {
    const CliRuntimes = (await import('../../../src/pages/ai/cli/CliRuntimes.vue')).default
    const wrapper = mount(CliRuntimes, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runtimes.list).toHaveBeenCalled())

    await (wrapper.vm as unknown as { probe: () => Promise<void> }).probe()
    expect(bridge.runtimes.probe).toHaveBeenCalled()
    expect(bridge.runtimes.list).toHaveBeenCalledTimes(2)
  })
})

describe('CliRuns', () => {
  it('lists runs', async () => {
    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const wrapper = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())
    expect(wrapper.exists()).toBe(true)
  })

  it('replays the event timeline when a run detail is opened', async () => {
    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const wrapper = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())

    const vm = wrapper.vm as unknown as {
      crud: { openView: (row: unknown) => void }
      events: { length: number }
    }
    vm.crud.openView(runs[1])

    await vi.waitFor(() => expect(bridge.runs.messages).toHaveBeenCalledWith('run-2'))
    await vi.waitFor(() => expect(vm.events.length).toBe(2))
  })

  it('pushes declared filters to the backend at the top level', async () => {
    // useCrudPage carries filters nested under `filters`, the API binds them at
    // the top level. If the page forwards the raw query the control renders,
    // submits, and changes nothing - the worst kind of filter, because it looks
    // like it works.
    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const wrapper = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())

    const vm = wrapper.vm as unknown as {
      crud: { setFilters: (f: Record<string, unknown>) => void; refresh: () => Promise<void> }
    }
    vm.crud.setFilters({ status: 'Failed' })
    await vm.crud.refresh()

    const sent = bridge.runs.list.mock.calls.at(-1)![0] as Record<string, unknown>
    expect(sent.status).toBe('Failed')
    expect(sent.filters).toBeUndefined()
  })

  it('only offers cancel on runs that have not finished', async () => {
    // Cancelling a finished run is a 409; offering it teaches the operator that
    // the button is unreliable.
    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const wrapper = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())

    const vm = wrapper.vm as unknown as {
      rowActions: { key: string; show?: (row: unknown) => boolean; onClick: (row: unknown) => Promise<void> }[]
    }
    const cancel = vm.rowActions.find((a) => a.key === 'cancel')!
    expect(cancel.show?.(runs[0])).toBe(true)
    expect(cancel.show?.(runs[1])).toBe(false)

    await cancel.onClick(runs[0])
    expect(bridge.runs.cancel).toHaveBeenCalledWith('run-1')
  })
})
