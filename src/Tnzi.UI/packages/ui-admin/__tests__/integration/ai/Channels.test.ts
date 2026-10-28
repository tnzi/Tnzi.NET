import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Channels integration test - TContentPage (card=false) with a four-tile KPI
 * strip + three tabs:
 *   • Adapters - registered channel adapters (name + streaming badge)
 *   • Connections - IGateway live WebSocket connections
 *   • Bindings - ISessionBinder session-binding rules (configuration + database)
 *
 * The admin client + the channels bridge are mocked so the page mounts without
 * a backend. The bridge exposes `channels.getStatus/getAdapters` and
 * `gateway.getStatus/getConnections/getBindings`; assertions run against the
 * mock data (KPI values, per-tab table row counts, empty + module-not-loaded
 * states).
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const getChannelStatus = vi.fn(async () => ({
  enabled: true,
  maxConcurrency: 8,
  streamingThrottleMs: 200,
  adapters: [
    { name: 'Telegram', supportsStreaming: true },
    { name: 'Slack', supportsStreaming: false },
  ],
}))
const getAdapters = vi.fn(async () => [
  { name: 'Telegram', supportsStreaming: true },
  { name: 'Slack', supportsStreaming: false },
  { name: 'Discord', supportsStreaming: true },
])
const getGatewayStatus = vi.fn(async () => ({
  enabled: true,
  connectedWebSocketCount: 5,
  activeSessionCount: 3,
}))
const getConnections = vi.fn(async () => [
  {
    connectionId: 'conn-001',
    userId: 'u-1',
    clientType: 'websocket',
    connectedAt: '2026-06-07T08:00:00Z',
  },
  {
    connectionId: 'conn-002',
    userId: null,
    clientType: 'websocket',
    connectedAt: '2026-06-07T08:05:00Z',
  },
])
const getBindings = vi.fn(async () => [
  {
    id: 'bind-1',
    channel: 'telegram',
    peerKind: 'group',
    peerId: 'g-42',
    agentId: 'agent-support',
    scope: 'PerChannelPeer',
    priority: 100,
    isEnabled: true,
    source: 'database',
  },
])

vi.mock('../../../src/services/bridges/channels-bridge', () => ({
  createChannelsBridge: () => ({
    channels: { getStatus: getChannelStatus, getAdapters },
    gateway: {
      getStatus: getGatewayStatus,
      getConnections,
      getBindings,
    },
  }),
}))

import Channels from '../../../src/pages/ai/channels/Channels.vue'

const stubs = {
  Tabs: {
    name: 'Tabs',
    props: ['value'],
    emits: ['update:value'],
    // Render every pane so we can assert across tabs without simulating
    // naive's pane-switching.
    template: '<div class="n-tabs-stub"><slot /></div>',
  },
  TabPane: { name: 'TabPane', props: ['name', 'tab'], template: '<div class="n-tab-pane-stub" :data-name="name"><slot /></div>' },
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot /></div>' },
  // The KPI strip now uses TKpiCard, which animates numeric values via
  // NNumberAnimation (tween from 0). Stub it to render the target synchronously
  // so the count assertions hold.
  NumberAnimation: { name: 'NumberAnimation', props: ['from', 'to', 'precision'], template: '<span>{{ to }}</span>' },
  Tag: { name: 'Tag', template: '<span class="n-tag-stub"><slot /></span>' },
  Alert: {
    name: 'Alert',
    props: ['title', 'type'],
    template: '<div class="n-alert-stub" :data-type="type" :data-title="title"><slot /></div>',
  },
  Empty: {
    name: 'Empty',
    props: ['description'],
    template: '<div class="n-empty-stub" :data-desc="description">{{ description }}</div>',
  },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  // Stub the responsive table wrapper so we can assert rendered row counts per
  // tab (and the column keys) without mounting naive's NDataTable.
  TResponsiveTable: {
    name: 'TResponsiveTable',
    props: ['data', 'columns', 'loading', 'rowKey'],
    template:
      '<div class="t-responsive-table-stub" :data-rows="(data || []).length" :data-cols="(columns || []).map((c) => c.key).join()"></div>',
  },
}

type Vm = {
  refresh: () => Promise<void>
  channelStatus: { enabled: boolean } | null
  gatewayStatus: { connectedWebSocketCount: number } | null
  adapters: Array<{ name: string }>
  connections: Array<{ connectionId: string }>
  bindings: Array<{ id: string }>
  moduleLoaded: boolean
}

function rowsByName(wrapper: ReturnType<typeof mount>): Record<string, number> {
  const out: Record<string, number> = {}
  for (const pane of wrapper.findAll('.n-tab-pane-stub')) {
    const name = pane.attributes('data-name') ?? ''
    const table = pane.find('.t-responsive-table-stub')
    out[name] = table.exists() ? Number(table.attributes('data-rows') ?? '0') : -1
  }
  return out
}

describe('Channels page (KPI strip + adapters / connections / bindings tabs)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getChannelStatus.mockClear()
    getAdapters.mockClear()
    getGatewayStatus.mockClear()
    getConnections.mockClear()
    getBindings.mockClear()
  })

  it('mounts and fetches channel + gateway data on mount', async () => {
    mount(Channels, { global: { stubs } })
    await flushPromises()
    expect(getChannelStatus).toHaveBeenCalledTimes(1)
    expect(getAdapters).toHaveBeenCalledTimes(1)
    expect(getGatewayStatus).toHaveBeenCalledTimes(1)
    expect(getConnections).toHaveBeenCalledTimes(1)
    expect(getBindings).toHaveBeenCalledTimes(1)
  })

  it('renders the four KPI tiles with adapter / connection / session / binding counts', async () => {
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    const values = wrapper
      .findAll('.t-stat-card__number')
      .map((s) => s.text())
    // adapters (3 from getAdapters), websocket connections (5), active sessions (3), bindings (1)
    expect(values).toEqual(['3', '5', '3', '1'])
  })

  it('Adapters tab renders one row per adapter (from getAdapters)', async () => {
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    expect(rowsByName(wrapper).adapters).toBe(3)
  })

  it('Connections tab renders the live gateway connections', async () => {
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    expect(rowsByName(wrapper).connections).toBe(2)
  })

  it('Bindings tab renders the session-binding rules', async () => {
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    expect(rowsByName(wrapper).bindings).toBe(1)
  })

  it('Bindings tab shows where each rule comes from, with a row key that tells configuration rules apart', async () => {
    // Configuration rules are not persisted: every one carries the empty GUID
    getBindings.mockResolvedValueOnce([
      { id: '00000000-0000-0000-0000-000000000000', channel: 'slack', peerKind: null, peerId: null, agentId: 'a-1', scope: 'PerPeer', priority: 5, isEnabled: true, source: 'config' },
      { id: '00000000-0000-0000-0000-000000000000', channel: 'discord', peerKind: null, peerId: null, agentId: 'a-1', scope: 'Global', priority: 1, isEnabled: true, source: 'config' },
      { id: 'bind-1', channel: 'telegram', peerKind: 'group', peerId: 'g-42', agentId: 'agent-support', scope: 'PerChannelPeer', priority: 100, isEnabled: true, source: 'database' },
    ] as never)
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()

    const pane = wrapper.findAll('.n-tab-pane-stub').find((p) => p.attributes('data-name') === 'bindings')!
    const table = pane.findComponent({ name: 'TResponsiveTable' })
    expect(table.attributes('data-cols')!.split(',')).toContain('source')

    const rowKey = table.props('rowKey') as (r: unknown) => string
    const keys = (table.props('data') as unknown[]).map(rowKey)
    expect(new Set(keys).size).toBe(3)
  })

  it('Bindings tab keys configuration rules apart even when they differ only in scope or not at all', async () => {
    // Every field but scope is shared by the first two; the last two are the
    // same rule written twice. All four are valid configuration.
    const emptyId = '00000000-0000-0000-0000-000000000000'
    const base = { id: emptyId, channel: 'slack', peerKind: 'group', peerId: 'g-1', agentId: 'a-1', priority: 5, isEnabled: true, source: 'config' }
    getBindings.mockResolvedValueOnce([
      { ...base, scope: 'PerPeer' },
      { ...base, scope: 'PerThread' },
      { ...base, scope: 'Global' },
      { ...base, scope: 'Global' },
      { id: 'bind-1', channel: 'slack', peerKind: 'group', peerId: 'g-1', agentId: 'a-1', scope: 'Global', priority: 5, isEnabled: true, source: 'database' },
    ] as never)
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()

    const pane = wrapper.findAll('.n-tab-pane-stub').find((p) => p.attributes('data-name') === 'bindings')!
    const table = pane.findComponent({ name: 'TResponsiveTable' })
    const rowKey = table.props('rowKey') as (r: unknown) => string
    const rows = table.props('data') as unknown[]
    const keys = rows.map(rowKey)
    expect(new Set(keys).size).toBe(5)
    // The key is a pure function of the row: asking again gives the same answer,
    // so selection and re-render stay pinned to the same rule.
    expect(rows.map(rowKey)).toEqual(keys)
  })

  it('Connections tab has no device node column (the field is gone from the wire)', async () => {
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    const pane = wrapper.findAll('.n-tab-pane-stub').find((p) => p.attributes('data-name') === 'connections')!
    expect(pane.find('.t-responsive-table-stub').attributes('data-cols')).not.toContain('deviceNodeId')
  })

  it('falls back to status.adapters when getAdapters returns empty', async () => {
    getAdapters.mockResolvedValueOnce([])
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    // status payload has 2 adapters → KPI + adapters table use the fallback
    expect((wrapper.vm as unknown as Vm).adapters).toHaveLength(2)
    expect(rowsByName(wrapper).adapters).toBe(2)
  })

  it('shows an empty state on the connections + bindings tabs when there are none', async () => {
    getConnections.mockResolvedValueOnce([])
    getBindings.mockResolvedValueOnce([])
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    const rows = rowsByName(wrapper)
    // -1 = no table rendered → empty state shown instead
    expect(rows.connections).toBe(-1)
    expect(rows.bindings).toBe(-1)
    expect(wrapper.findAll('.n-empty-stub').length).toBeGreaterThanOrEqual(2)
  })

  it('shows a "module not enabled" notice when the module is not loaded (status null)', async () => {
    getChannelStatus.mockResolvedValueOnce(null as never)
    getAdapters.mockResolvedValueOnce([])
    getGatewayStatus.mockResolvedValueOnce(null as never)
    getConnections.mockResolvedValueOnce([])
    getBindings.mockResolvedValueOnce([])
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.moduleLoaded).toBe(false)
    const info = wrapper.findAll('.n-alert-stub').find((a) => a.attributes('data-type') === 'info')
    expect(info?.exists()).toBe(true)
    // tabs are not rendered in the unavailable state
    expect(wrapper.find('.n-tabs-stub').exists()).toBe(false)
  })

  it('surfaces a warning when channels is loaded but disabled in config', async () => {
    getChannelStatus.mockResolvedValueOnce({
      enabled: false,
      maxConcurrency: 8,
      streamingThrottleMs: 200,
      adapters: [],
    })
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    const warn = wrapper.findAll('.n-alert-stub').find((a) => a.attributes('data-type') === 'warning')
    expect(warn?.exists()).toBe(true)
  })

  it('reports a refused or failed read instead of claiming the module is not loaded', async () => {
    // The bridge answers null / [] only for an absent module (404 / 501);
    // anything else rejects with the server's reason.
    const refusal = new Error('You do not have permission to read the gateway')
    getChannelStatus.mockResolvedValueOnce({ enabled: true, maxConcurrency: 8, streamingThrottleMs: 200, adapters: [] })
    getAdapters.mockResolvedValueOnce([])
    getGatewayStatus.mockRejectedValueOnce(refusal)
    getConnections.mockRejectedValueOnce(refusal)
    getBindings.mockRejectedValueOnce(refusal)
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm & { loadError: string | null }
    expect(vm.channelStatus).toBeNull()
    expect(vm.adapters).toHaveLength(0)
    expect(vm.connections).toHaveLength(0)
    expect(vm.bindings).toHaveLength(0)
    expect(vm.loadError).toBe('You do not have permission to read the gateway')
    const error = wrapper.findAll('.n-alert-stub').find((a) => a.attributes('data-type') === 'error')
    expect(error?.text()).toContain('You do not have permission to read the gateway')
    // Not the "module not enabled" notice: the module answered.
    expect(wrapper.findAll('.n-alert-stub').some((a) => a.attributes('data-type') === 'info')).toBe(false)
  })
})
