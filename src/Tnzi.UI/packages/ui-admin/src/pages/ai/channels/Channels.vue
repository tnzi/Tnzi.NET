<template>
  <!--
    Channels — surfaces /admin/channels/* and /admin/gateway/* exposed by
    `Tnzi.AI.Channels`. Two tabs:
      • Adapters — Tnzi.AI.Channels module status + per-adapter capabilities
      • Gateway  — IGateway control-plane (WebSocket connections + session
                   binding rules)
  -->
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <template #actions>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <template #default>
      <div class="t-channels-page__kpis">
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('kpi.adapters')" :value="channelStatus?.adapters.length ?? 0">
            <template #suffix>
              <NTag size="small" :type="channelStatus?.enabled ? 'success' : 'default'" :bordered="false">
                {{ channelStatus?.enabled ? t('status.enabled') : t('status.disabled') }}
              </NTag>
            </template>
          </NStatistic>
        </NCard>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('kpi.gatewayConn')" :value="gatewayStatus?.connectedWebSocketCount ?? 0">
            <template #suffix>
              <NTag size="small" :type="gatewayStatus?.enabled ? 'success' : 'default'" :bordered="false">
                {{ gatewayStatus?.enabled ? t('status.enabled') : t('status.disabled') }}
              </NTag>
            </template>
          </NStatistic>
        </NCard>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('kpi.activeSessions')" :value="gatewayStatus?.activeSessionCount ?? 0" />
        </NCard>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('kpi.bindings')" :value="bindings.length" />
        </NCard>
      </div>

      <NAlert
        v-if="channelStatus && !channelStatus.enabled"
        :title="t('disabled.channels.title')"
        type="warning"
        :closable="false"
        class="mb-12px"
      >
        {{ t('disabled.channels.body') }}
      </NAlert>

      <NTabs v-model:value="activeTab" type="line" animated class="t-table-tabs">
        <NTabPane name="adapters" :tab="t('tabs.adapters')">
          <div class="t-table-tabs__pane">
            <TResponsiveTable
              :columns="adapterColumns"
              :data="channelStatus?.adapters ?? []"
              :loading="loading"
              :pagination="false"
              :bordered="false"
              size="small"
              :flex-height="true"
            />
            <div v-if="channelStatus" class="t-channels-page__meta">
              <span>{{ t('meta.maxConcurrency', { n: channelStatus.maxConcurrency }) }}</span>
              <span>·</span>
              <span>{{ t('meta.throttle', { ms: channelStatus.streamingThrottleMs }) }}</span>
            </div>
          </div>
        </NTabPane>

        <NTabPane name="connections" :tab="t('tabs.connections')">
          <div class="t-table-tabs__pane">
            <TResponsiveTable
              :columns="connectionColumns"
              :data="connections"
              :loading="loading"
              :pagination="{ pageSize: 15 }"
              :bordered="false"
              size="small"
              :flex-height="true"
            />
          </div>
        </NTabPane>

        <NTabPane name="bindings" :tab="t('tabs.bindings')">
          <div class="t-table-tabs__pane">
            <TResponsiveTable
              :columns="bindingColumns"
              :data="bindings"
              :loading="loading"
              :pagination="{ pageSize: 15 }"
              :bordered="false"
              size="small"
              :flex-height="true"
            />
          </div>
        </NTabPane>
      </NTabs>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NStatistic,
  NTabPane,
  NTabs,
  NTag,
} from 'naive-ui'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
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
import { interpolate, translatePageKey } from '../../_shared/translate'
import TContentPage from '../../../components/layout/TContentPage.vue'

const bridge = createChannelsBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('ai.channels', key), params)

const loading = ref(false)
const activeTab = ref<'adapters' | 'connections' | 'bindings'>('adapters')
const channelStatus = ref<ChannelModuleStatusDto | null>(null)
const gatewayStatus = ref<GatewayStatusDto | null>(null)
const connections = ref<GatewayConnectionInfo[]>([])
const bindings = ref<SessionBindingRuleDto[]>([])

function scopeLabel(s: number): string {
  // SessionScope enum: 0=Global / 1=PerPeer / 2=PerChannelPeer / 3=PerThread
  switch (s) {
    case 0: return t('scope.global')
    case 1: return t('scope.perPeer')
    case 2: return t('scope.perChannelPeer')
    case 3: return t('scope.perThread')
    default: return String(s)
  }
}

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
    width: 220,
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
      h(NTag, { size: 'small', bordered: false, type: row.supportsStreaming ? 'success' : 'default' }, () =>
        row.supportsStreaming ? t('status.supported') : t('status.notSupported'),
      ),
  },
]

const connectionColumns: DataTableColumns<GatewayConnectionInfo> = [
  {
    title: () => t('cols.connectionId'),
    key: 'connectionId',
    width: 220,
    render: (row) =>
      h('code', { class: 'font-[family-name:var(--tnzi-font-mono)] text-11px' }, row.connectionId),
  },
  { title: () => t('cols.clientType'), key: 'clientType', width: 120 },
  { title: () => t('cols.user'), key: 'userId', width: 220, render: (r) => r.userId ?? '—' },
  { title: () => t('cols.deviceNode'), key: 'deviceNodeId', width: 180, render: (r) => r.deviceNodeId ?? '—' },
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
      h(NTag, { size: 'tiny', bordered: false, type: r.isEnabled ? 'success' : 'default' }, () =>
        r.isEnabled ? t('status.enabled') : t('status.disabled'),
      ),
  },
  { title: () => t('cols.channel'), key: 'channel', width: 140, render: (r) => r.channel ?? '*' },
  { title: () => t('cols.peerKind'), key: 'peerKind', width: 100, render: (r) => r.peerKind ?? '*' },
  { title: () => t('cols.peerId'), key: 'peerId', width: 160, render: (r) => r.peerId ?? '*' },
  {
    title: () => t('cols.scope'),
    key: 'scope',
    width: 140,
    render: (r) => h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => scopeLabel(r.scope)),
  },
  {
    title: () => t('cols.agentId'),
    key: 'agentId',
    ellipsis: { tooltip: true },
    render: (r) => h('code', { class: 'font-[family-name:var(--tnzi-font-mono)] text-11px' }, r.agentId),
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [cs, gs, conns, binds] = await Promise.all([
      bridge.channels.getStatus(),
      bridge.gateway.getStatus(),
      bridge.gateway.getConnections(),
      bridge.gateway.getBindings(),
    ])
    channelStatus.value = cs
    gatewayStatus.value = gs
    connections.value = conns
    bindings.value = binds
  } catch {
    channelStatus.value = null
    gatewayStatus.value = null
    connections.value = []
    bindings.value = []
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
.t-channels-page__kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
@media (max-width: 900px) {
  .t-channels-page__kpis { grid-template-columns: repeat(2, 1fr); }
}
.t-channels-page__meta {
  margin-top: 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  display: flex;
  gap: 8px;
}
</style>
