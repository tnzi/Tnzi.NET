<template>
  <!--
    Channels - surfaces /admin/channels/* and /admin/gateway/* exposed by
    `Tnzi.AI.Channels`. A KPI strip plus three tabs:
      • Adapters - registered channel adapters (name + streaming capability)
      • Connections - IGateway live WebSocket connections (control plane)
      • Bindings - ISessionBinder 4-scope session-binding rules

    The module is an optional sub-module of Tnzi.AI; when it is not loaded the
    bridge surfaces `null`/empty values and the page renders a friendly
    "module not enabled" notice instead of failing.
  -->
  <TTabsPage
    v-if="loading || moduleLoaded"
    :title="t('title')"
    :translate="t"
    :sections="tabs"
    default-section="adapters"
  >
    <template #actions>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <!-- Page-level summary + config notice sit above the tab surface, on the
         canvas, visible on every tab. -->
    <template #kpis>
      <TKpiRow cols="1 s:2 m:4">
        <TKpiCard :label="t('kpi.adapters')" :value="adapters.length">
          <template #extra>
            <NTag size="small" :type="channelStatus?.enabled ? 'success' : 'default'" :bordered="false">
              {{ channelStatus?.enabled ? t('status.enabled') : t('status.disabled') }}
            </NTag>
          </template>
        </TKpiCard>
        <TKpiCard :label="t('kpi.gatewayConn')" :value="gatewayStatus?.connectedWebSocketCount ?? null">
          <template #extra>
            <NTag size="small" :type="gatewayStatus?.enabled ? 'success' : 'default'" :bordered="false">
              {{ gatewayStatus?.enabled ? t('status.enabled') : t('status.disabled') }}
            </NTag>
          </template>
        </TKpiCard>
        <TKpiCard :label="t('kpi.activeSessions')" :value="gatewayStatus?.activeSessionCount ?? null" />
        <TKpiCard :label="t('kpi.bindings')" :value="bindings.length" />
      </TKpiRow>

      <!-- Channels loaded but disabled by configuration. -->
      <NAlert
        v-if="channelStatus && !channelStatus.enabled"
        :title="t('disabled.channels.title')"
        type="warning"
        :closable="false"
        class="mt-12px"
      >
        {{ t('disabled.channels.body') }}
      </NAlert>
    </template>

    <!-- ── Adapters ─────────────────────────────────────────── -->
    <template #adapters>
      <NEmpty
        v-if="!loading && adapters.length === 0"
        :description="t('empty.adapters')"
        class="my-32px"
      />
      <template v-else>
        <TResponsiveTable
          :columns="adapterColumns"
          :data="adapters"
          :loading="loading"
          :pagination="false"
          :bordered="false"
          size="small"
          :flex-height="true"
        />
        <div v-if="channelStatus" class="mt-8px flex gap-8px text-12px text-muted">
          <span>{{ t('meta.maxConcurrency', { n: channelStatus.maxConcurrency }) }}</span>
          <span>·</span>
          <span>{{ t('meta.throttle', { ms: channelStatus.streamingThrottleMs }) }}</span>
        </div>
      </template>
    </template>

    <!-- ── Connections ──────────────────────────────────────── -->
    <template #connections>
      <NEmpty
        v-if="!loading && connections.length === 0"
        :description="t('empty.connections')"
        class="my-32px"
      />
      <TResponsiveTable
        v-else
        :columns="connectionColumns"
        :data="connections"
        :loading="loading"
        :pagination="{ pageSize: 15 }"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </template>

    <!-- ── Bindings ─────────────────────────────────────────── -->
    <template #bindings>
      <NEmpty
        v-if="!loading && bindings.length === 0"
        :description="t('empty.bindings')"
        class="my-32px"
      />
      <TResponsiveTable
        v-else
        :columns="bindingColumns"
        :data="bindings"
        :loading="loading"
        :pagination="{ pageSize: 15 }"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </template>
  </TTabsPage>

  <!-- Module not loaded (getStatus → null / 404): friendly "unavailable" state.
       The page header (title + refresh) is preserved; no tabs are rendered. -->
  <TContentPage
    v-else
    :title="t('title')"
    :translate="t"
    :card="false"
    scroll="fill"
  >
    <template #actions>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <NAlert
      :title="t('disabled.notLoaded.title')"
      type="info"
      :closable="false"
    >
      {{ t('disabled.notLoaded.body') }}
    </NAlert>
  </TContentPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, h, onMounted, ref } from 'vue'
import {
  NAlert,
  NButton,
  NEmpty,
  NTag,
} from 'naive-ui'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import { TKpiCard, TKpiRow } from '../../../components/data'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon, type StatusType } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../../plugin/client'
import {
  createChannelsBridge,
  type ChannelAdapterDto,
  type ChannelModuleStatusDto,
  type GatewayConnectionInfo,
  type GatewayStatusDto,
  type SessionBindingRuleDto,
} from '../../../services/bridges/channels-bridge'
import { makePageTranslator } from '../../_shared/translate'
import TContentPage from '../../../components/layout/TContentPage.vue'
import TTabsPage, { type TabSection } from '../../../components/layout/TTabsPage.vue'

const bridge = createChannelsBridge({ client: useAdminClient() })
const t = makePageTranslator('ai.channels')

const loading = ref(false)
// Primary tabs. TTabsPage owns the `?section=` deep-linking + Back/Forward.
const tabs: TabSection[] = [
  { name: 'adapters', label: t('tabs.adapters') },
  { name: 'connections', label: t('tabs.connections') },
  { name: 'bindings', label: t('tabs.bindings') },
]
const channelStatus = ref<ChannelModuleStatusDto | null>(null)
const gatewayStatus = ref<GatewayStatusDto | null>(null)
const adapters = ref<ChannelAdapterDto[]>([])
const connections = ref<GatewayConnectionInfo[]>([])
const bindings = ref<SessionBindingRuleDto[]>([])

// Absolute i18n namespace - TStatusBadge's admin wrapper resolves `labelKey`
// from the locale root, so mapping keys must be fully qualified.
const CH_NS = 'admin.modules.ai.channels'

// SessionScope enum → unified status pill. The backend serialises it as the
// PascalCase member NAME (JsonStringEnumConverter: Global / PerPeer /
// PerChannelPeer / PerThread); the legacy numeric ordinals are kept too so
// TStatusBadge's `String(value)` lookup resolves regardless of wire form. All
// scopes share the neutral `info` tone (matches the pre-migration look).
const scopeBadgeMapping: Record<string, { type: StatusType; labelKey: string }> = {
  0: { type: 'info', labelKey: `${CH_NS}.scope.global` },
  1: { type: 'info', labelKey: `${CH_NS}.scope.perPeer` },
  2: { type: 'info', labelKey: `${CH_NS}.scope.perChannelPeer` },
  3: { type: 'info', labelKey: `${CH_NS}.scope.perThread` },
  Global: { type: 'info', labelKey: `${CH_NS}.scope.global` },
  PerPeer: { type: 'info', labelKey: `${CH_NS}.scope.perPeer` },
  PerChannelPeer: { type: 'info', labelKey: `${CH_NS}.scope.perChannelPeer` },
  PerThread: { type: 'info', labelKey: `${CH_NS}.scope.perThread` },
}

/**
 * The module counts as "loaded" when either control plane reports a status.
 * `Tnzi.AI.Channels` not loaded → both statuses are null (bridge swallows the
 * 404), and we show the "module not enabled" notice instead of the tabs.
 */
const moduleLoaded = computed(() => channelStatus.value !== null || gatewayStatus.value !== null)

function adapterIcon(name: string): string {
  switch ((name ?? '').toLowerCase()) {
    case 'telegram': return 'mdi:send-circle-outline'
    case 'slack': return 'mdi:slack'
    case 'discord': return 'mdi:discord'
    case 'feishu': return 'mdi:chat-outline'
    case 'dingtalk':
    case 'ding': return 'mdi:bell-outline'
    default: return 'mdi:lan-connect'
  }
}

const adapterColumns: DataTableColumns<ChannelAdapterDto> = [
  {
    title: () => t('cols.name'),
    key: 'name',
    minWidth: 150,
    render: (row) =>
      h('div', { class: 'flex items-center gap-8px' }, [
        h(TSvgIcon, { icon: adapterIcon(row.name), size: 16 }),
        h('span', { class: 'font-500' }, row.name),
      ]),
  },
  {
    title: () => t('cols.streaming'),
    key: 'supportsStreaming',
    width: 140,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.supportsStreaming),
        mapping: {
          true: { type: 'success', labelKey: `${CH_NS}.status.supported` },
          false: { type: 'default', labelKey: `${CH_NS}.status.notSupported` },
        },
      }),
  },
]

const connectionColumns: DataTableColumns<GatewayConnectionInfo> = [
  {
    title: () => t('cols.connectionId'),
    key: 'connectionId',
    width: 220,
    render: (row) =>
      h('code', { class: 'tnzi-mono text-11px' }, row.connectionId),
  },
  { title: () => t('cols.clientType'), key: 'clientType', width: 120 },
  { title: () => t('cols.user'), key: 'userId', width: 220, render: (r) => r.userId ?? EMPTY_DASH },
  { title: () => t('cols.deviceNode'), key: 'deviceNodeId', width: 180, render: (r) => r.deviceNodeId ?? EMPTY_DASH },
  {
    title: () => t('cols.connectedAt'),
    key: 'connectedAt',
    width: 170,
    render: (r) => formatDate(r.connectedAt),
  },
]

const bindingColumns: DataTableColumns<SessionBindingRuleDto> = [
  {
    title: () => t('cols.priority'),
    key: 'priority',
    width: 90,
    align: 'right',
    sorter: (a, b) => a.priority - b.priority,
    defaultSortOrder: 'descend',
  },
  {
    title: () => t('cols.isEnabled'),
    key: 'isEnabled',
    width: 90,
    render: (r) =>
      h(TStatusBadge, {
        value: Boolean(r.isEnabled),
        size: 'tiny',
        mapping: {
          true: { type: 'success', labelKey: `${CH_NS}.status.enabled` },
          false: { type: 'default', labelKey: `${CH_NS}.status.disabled` },
        },
      }),
  },
  { title: () => t('cols.channel'), key: 'channel', width: 140, render: (r) => r.channel ?? '*' },
  { title: () => t('cols.peerKind'), key: 'peerKind', width: 100, render: (r) => r.peerKind ?? '*' },
  { title: () => t('cols.peerId'), key: 'peerId', width: 160, render: (r) => r.peerId ?? '*' },
  {
    title: () => t('cols.scope'),
    key: 'scope',
    width: 140,
    render: (r) => h(TStatusBadge, { value: r.scope, size: 'tiny', mapping: scopeBadgeMapping }),
  },
  {
    title: () => t('cols.agentId'),
    key: 'agentId',
    ellipsis: { tooltip: true },
    render: (r) => h('code', { class: 'tnzi-mono text-11px' }, r.agentId),
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [cs, ads, gs, conns, binds] = await Promise.all([
      bridge.channels.getStatus(),
      bridge.channels.getAdapters(),
      bridge.gateway.getStatus(),
      bridge.gateway.getConnections(),
      bridge.gateway.getBindings(),
    ])
    channelStatus.value = cs
    gatewayStatus.value = gs
    // Prefer the dedicated adapters endpoint; fall back to the status payload.
    adapters.value = ads.length > 0 ? ads : (cs?.adapters ?? [])
    connections.value = conns
    bindings.value = binds
  } catch {
    channelStatus.value = null
    gatewayStatus.value = null
    adapters.value = []
    connections.value = []
    bindings.value = []
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })

defineExpose({ refresh, channelStatus, gatewayStatus, adapters, connections, bindings, moduleLoaded })
</script>
