import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Channels integration test — TContentPage (card=false) with a four-tile KPI
 * strip + three tabs:
 *   • Adapters    — registered channel adapters (name + streaming badge)
 *   • Connections — IGateway live WebSocket connections
 *   • Bindings    — ISessionBinder session-binding rules
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
    clientType: 'web',
    deviceNodeId: null,
    connectedAt: '2026-06-07T08:00:00Z',
  },
  {
    connectionId: 'conn-002',
    userId: null,
    clientType: 'device',
    deviceNodeId: 'node-win-01',
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
    scope: 2,
    priority: 100,
    isEnabled: true,
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
  // tab without mounting naive's NDataTable.
  TResponsiveTable: {
    name: 'TResponsiveTable',
    props: ['data', 'columns', 'loading'],
    template: '<div class="t-responsive-table-stub" :data-rows="(data || []).length"></div>',
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

  it('degrades to empty state when the bridge throws (network / 404)', async () => {
    getChannelStatus.mockRejectedValueOnce(new Error('404'))
    getAdapters.mockRejectedValueOnce(new Error('404'))
    getGatewayStatus.mockRejectedValueOnce(new Error('404'))
    getConnections.mockRejectedValueOnce(new Error('404'))
    getBindings.mockRejectedValueOnce(new Error('404'))
    const wrapper = mount(Channels, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as Vm
    expect(vm.channelStatus).toBeNull()
    expect(vm.adapters).toHaveLength(0)
    expect(vm.connections).toHaveLength(0)
    expect(vm.bindings).toHaveLength(0)
    expect(vm.moduleLoaded).toBe(false)
  })
})
