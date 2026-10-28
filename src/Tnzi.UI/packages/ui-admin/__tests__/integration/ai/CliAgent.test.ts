import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

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
    get: vi.fn(async (id: string) => runs.find((r) => r.id === id)!),
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

  it('the search box filters the loaded runtimes: the endpoint takes no keyword', async () => {
    bridge.runtimes.list.mockResolvedValueOnce([
      ...runtimes,
      { ...runtimes[0], id: 'rt-2', hostId: 'DEV-07', name: 'Gemini @ DEV-07', providerKey: 'gemini', providerDisplayName: 'Gemini CLI', executablePath: 'C:/tools/gemini.cmd' },
    ])
    const CliRuntimes = (await import('../../../src/pages/ai/cli/CliRuntimes.vue')).default
    const wrapper = mount(CliRuntimes, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runtimes.list).toHaveBeenCalled())
    const crud = (wrapper.vm as unknown as {
      crud: { items: { value: Array<{ id: string }> }; setSearch: (text: string) => void; refresh: () => Promise<void> }
    }).crud
    await vi.waitFor(() => expect(crud.items.value).toHaveLength(2))

    bridge.runtimes.list.mockResolvedValueOnce([
      ...runtimes,
      { ...runtimes[0], id: 'rt-2', hostId: 'DEV-07', name: 'Gemini @ DEV-07', providerKey: 'gemini', providerDisplayName: 'Gemini CLI', executablePath: 'C:/tools/gemini.cmd' },
    ])
    crud.setSearch('dev-07')
    await crud.refresh()
    expect(crud.items.value.map((r) => r.id)).toEqual(['rt-2'])
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

  // The date filters arrive as picked calendar dates; runs are filtered by
  // their creation timestamp, so the range goes out as the bounds of those
  // local days and a one-day range covers the whole day.
  it('sends a date range as the bounds of those local days', async () => {
    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const wrapper = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())

    const vm = wrapper.vm as unknown as {
      crud: { setFilters: (f: Record<string, unknown>) => void; refresh: () => Promise<void> }
    }
    vm.crud.setFilters({ startTime: '2026-10-10', endTime: '2026-10-10' })
    await vm.crud.refresh()

    const sent = bridge.runs.list.mock.calls.at(-1)![0] as Record<string, unknown>
    expect(sent.startTime).toBe(new Date(2026, 9, 10).toISOString())
    expect(sent.endTime).toBe(new Date(2026, 9, 10, 23, 59, 59, 999).toISOString())
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

  it('offers no cancel to a reader without ai.cliRun.execute', async () => {
    useAdminAuthStore().setUserInfo({ id: 'r', username: 'reader', roles: [], permissions: ['ai.cliRun.view'] })
    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const wrapper = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())

    const vm = wrapper.vm as unknown as { rowActions: { key: string; show?: (row: unknown) => boolean }[] }
    expect(vm.rowActions.find((a) => a.key === 'cancel')!.show?.(runs[0])).toBe(false)
  })
})

describe('CLI write affordances follow the permission codes', () => {
  it('offers no probe to a reader without ai.cliRuntime.execute', async () => {
    useAdminAuthStore().setUserInfo({ id: 'r', username: 'reader', roles: [], permissions: ['ai.cliRuntime.view'] })
    const CliRuntimes = (await import('../../../src/pages/ai/cli/CliRuntimes.vue')).default
    const wrapper = mount(CliRuntimes, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runtimes.list).toHaveBeenCalled())
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).not.toContain('Probe')
  })
})

// The runtime edit form declared its labels and status options as raw keys with
// no translator, and both page titles were English literals: the operator read
// "columns.name" and "status.online", and a Chinese UI kept English titles.
describe('CLI pages speak the active locale', () => {
  it('the runtime form labels and status options are translated', async () => {
    const { useAdminAppStore } = await import('../../../src/stores/useAdminAppStore')
    useAdminAppStore().locale = 'zh-cn'
    const CliRuntimes = (await import('../../../src/pages/ai/cli/CliRuntimes.vue')).default
    const wrapper = mount(CliRuntimes, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runtimes.list).toHaveBeenCalled())

    const vm = wrapper.vm as unknown as { crud: { openEdit: (row: unknown) => void } }
    vm.crud.openEdit(runtimes[0])
    // The edit modal teleports to <body>; the page itself is not attached there.
    await vi.waitFor(() => expect(document.body.innerHTML).toContain('n-form-item'))

    const form = document.body.innerHTML
    expect(form).not.toContain('columns.')
    expect(form).toContain('最大并发')
    const select = wrapper.findAllComponents({ name: 'Select' }).find((c) =>
      (c.props('options') as Array<{ value: unknown }> | undefined)?.some((o) => o.value === 'Online'),
    )
    expect(select).toBeTruthy()
    expect((select!.props('options') as Array<{ label: string }>).map((o) => o.label)).toEqual(['在线', '已停用'])
    wrapper.unmount()
  })

  it('both page titles follow the locale', async () => {
    const { useAdminAppStore } = await import('../../../src/stores/useAdminAppStore')
    useAdminAppStore().locale = 'zh-cn'
    const CliRuntimes = (await import('../../../src/pages/ai/cli/CliRuntimes.vue')).default
    const runtimesPage = mount(CliRuntimes, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runtimes.list).toHaveBeenCalled())
    expect(runtimesPage.text()).toContain('外部 CLI 运行时')

    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const runsPage = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())
    expect(runsPage.text()).toContain('外部执行记录')
  })
})

describe('CliRuns timeline read failures', () => {
  it('a refused timeline is reported instead of reading as "no events"', async () => {
    bridge.runs.messages.mockRejectedValueOnce(new Error('Forbidden'))
    const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
    const wrapper = mount(CliRuns, { global: { stubs } })
    await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())

    const vm = wrapper.vm as unknown as {
      crud: { openView: (row: unknown) => void }
      eventsError: string | null
    }
    vm.crud.openView(runs[1])
    await vi.waitFor(() => expect(vm.eventsError).toBe('Forbidden'))
  })
})

// The drawer's poll re-reads the run as well as its timeline: the drawer shows
// the run's status, output and completion time, and a run that finishes while
// it is open has nothing more to poll for.
describe('CliRuns drawer on a running run', () => {
  it('follows the run to completion, then stops polling', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    try {
      const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
      const wrapper = mount(CliRuns, { global: { stubs } })
      await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())
      const vm = wrapper.vm as unknown as {
        crud: {
          openView: (row: unknown) => void
          formModal: { formData: { value: Record<string, unknown> | null } }
        }
      }
      vm.crud.openView(runs[0])
      await vi.waitFor(() => expect(bridge.runs.messages).toHaveBeenCalledTimes(1))

      bridge.runs.get.mockResolvedValueOnce({
        ...runs[0]!,
        status: 'Completed',
        output: 'All tests green.',
        completedAt: '2026-07-31T10:06:00Z',
      } as never)
      vi.advanceTimersByTime(3000)
      await vi.waitFor(() => expect(vm.crud.formModal.formData.value?.status).toBe('Completed'))
      expect(bridge.runs.get).toHaveBeenCalledWith('run-1')
      expect(vm.crud.formModal.formData.value?.output).toBe('All tests green.')

      const calls = bridge.runs.messages.mock.calls.length
      vi.advanceTimersByTime(9000)
      await Promise.resolve()
      expect(bridge.runs.messages.mock.calls.length).toBe(calls)
      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('CLI run list columns', () => {
  it('render the queue time, status, duration and cost for reading', async () => {
    const { runColumns } = await import('../../../src/pages/ai/cli/cli-runtime-config')
    const column = (key: string) => runColumns.find((c) => c.key === key)!
    const finished = runs[1] as unknown as Record<string, unknown>

    const queuedAt = column('creationTime').render?.(finished)
    expect(typeof queuedAt).toBe('string')
    expect(queuedAt).not.toContain('T09:30')

    const status = column('status').render?.(finished) as { props?: Record<string, unknown> } | null
    expect(status?.props?.labelKey).toBe('admin.modules.ai.cliRuns.runStatus.completed')
    expect(status?.props?.type).toBe('success')

    expect(column('durationMs').render?.(finished)).toBe('6400 ms')
    expect(column('estimatedCostUsd').render?.(finished)).toBe('$0.1147')
  })
})

// A running run's timeline polls while its drawer is open. Kept alive in its
// tab, the page stays mounted while another page is on screen; the poll must
// stop there and resume when the page is shown again.
describe('CliRuns timeline polling while the page is kept in the background', () => {
  it('stops on deactivation and resumes on activation', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    try {
      const { KeepAlive, defineComponent, h, ref } = await import('vue')
      const CliRuns = (await import('../../../src/pages/ai/cli/CliRuns.vue')).default
      const shown = ref(true)
      const Other = defineComponent({ name: 'OtherPage', render: () => h('div', 'other') })
      const Host = defineComponent({
        setup: () => () => h(KeepAlive, null, { default: () => (shown.value ? h(CliRuns) : h(Other)) }),
      })
      const wrapper = mount(Host, { global: { stubs } })
      await vi.waitFor(() => expect(bridge.runs.list).toHaveBeenCalled())

      const page = wrapper.findComponent(CliRuns)
      ;(page.vm as unknown as { crud: { openView: (row: unknown) => void } }).crud.openView(runs[0])
      await vi.waitFor(() => expect(bridge.runs.messages).toHaveBeenCalledTimes(1))

      vi.advanceTimersByTime(3000)
      await vi.waitFor(() => expect(bridge.runs.messages).toHaveBeenCalledTimes(2))

      shown.value = false
      await vi.waitFor(() => expect(wrapper.text()).toContain('other'))
      vi.advanceTimersByTime(9000)
      expect(bridge.runs.messages).toHaveBeenCalledTimes(2)

      shown.value = true
      await vi.waitFor(() => expect(bridge.runs.messages).toHaveBeenCalledTimes(3))
      vi.advanceTimersByTime(3000)
      await vi.waitFor(() => expect(bridge.runs.messages).toHaveBeenCalledTimes(4))
      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })
})
