<template>
  <!--
    DeviceList — surfaces /admin/devices* exposed by `Tnzi.AI.Device`.
    Two tabs:
      • Connected — registered device nodes with platform / state / capabilities
      • Pending pairings — open requests waiting on operator approval, each
                            row showing the 8-char code the device displays
                            plus approve/reject actions.
  -->
  <div class="t-devices-page t-stack-page">
    <div class="t-devices-page__header">
      <div class="t-devices-page__title">
        <TSvgIcon icon="mdi:devices" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-devices-page__toolbar">
        <NButton size="small" :loading="loading" @click="refresh">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
      </div>
    </div>

    <div class="t-devices-page__kpis">
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.connected')" :value="connectedCount" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.allDevices')" :value="devices.length" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.pending')" :value="pendings.length">
          <template #suffix>
            <NTag v-if="pendings.length > 0" size="small" type="warning" :bordered="false">
              {{ t('kpi.actionNeeded') }}
            </NTag>
          </template>
        </NStatistic>
      </NCard>
    </div>

    <NTabs v-model:value="activeTab" type="line" animated class="t-table-tabs">
      <NTabPane name="devices" :tab="t('tabs.devices')">
        <div class="t-table-tabs__pane">
          <NDataTable
            :columns="deviceColumns"
            :data="devices"
            :loading="loading"
            :pagination="{ pageSize: 15 }"
            :bordered="false"
            size="small"
            :flex-height="true"
          />
        </div>
      </NTabPane>

      <NTabPane name="pairings">
        <template #tab>
          <span style="display: inline-flex; align-items: center; gap: 6px">
            {{ t('tabs.pairings') }}
            <NBadge v-if="pendings.length > 0" :value="pendings.length" type="warning" :max="99" :show-zero="false" />
          </span>
        </template>
        <div class="t-table-tabs__pane">
          <NDataTable
            :columns="pairingColumns"
            :data="pendings"
            :loading="loading"
            :pagination="{ pageSize: 15 }"
            :bordered="false"
            size="small"
            :flex-height="true"
          />
        </div>
      </NTabPane>
    </NTabs>
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NBadge,
  NButton,
  NCard,
  NDataTable,
  NPopconfirm,
  NStatistic,
  NTabPane,
  NTabs,
  NTag,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../../plugin/client'
import {
  createDeviceBridge,
  type DeviceConnectionState,
  type DeviceNodeDto,
  type DevicePlatform,
  type PairingRequestDto,
} from '../../../services/bridges/device-bridge'
import { interpolate, translatePageKey } from '../../_shared/translate'

const bridge = createDeviceBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('ai.devices', key), params)

const loading = ref(false)
const activeTab = ref<'devices' | 'pairings'>('devices')
const devices = ref<DeviceNodeDto[]>([])
const pendings = ref<PairingRequestDto[]>([])

const connectedCount = computed(
  () => devices.value.filter((d) => d.state === 0).length,
)

function platformIcon(p: DevicePlatform): string {
  switch (p) {
    case 1: return 'mdi:microsoft-windows'
    case 2: return 'mdi:apple'
    case 3: return 'mdi:linux'
    case 4: return 'mdi:apple-ios'
    case 5: return 'mdi:android'
    default: return 'mdi:help-circle-outline'
  }
}
function platformLabel(p: DevicePlatform): string {
  switch (p) {
    case 1: return 'Windows'
    case 2: return 'macOS'
    case 3: return 'Linux'
    case 4: return 'iOS'
    case 5: return 'Android'
    default: return t('platform.unknown')
  }
}
function stateTone(s: DeviceConnectionState): 'success' | 'warning' | 'default' {
  switch (s) {
    case 0: return 'success'
    case 2: return 'warning'
    default: return 'default'
  }
}
function stateLabel(s: DeviceConnectionState): string {
  switch (s) {
    case 0: return t('state.connected')
    case 1: return t('state.disconnected')
    case 2: return t('state.pairing')
    default: return String(s)
  }
}
function formatDate(iso: string | null | undefined): string {
  if (!iso) return ''
  try { return new Date(iso).toLocaleString() } catch { return iso }
}

const deviceColumns: DataTableColumns<DeviceNodeDto> = [
  {
    title: () => t('cols.state'),
    key: 'state',
    width: 120,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: stateTone(row.state) }, () => stateLabel(row.state)),
  },
  {
    title: () => t('cols.name'),
    key: 'name',
    width: 200,
    render: (row) =>
      h('div', { style: 'display: flex; align-items: center; gap: 8px;' }, [
        h(TSvgIcon, { icon: platformIcon(row.platform), size: 16 }),
        h('span', { style: 'font-weight: 500' }, row.name),
      ]),
  },
  {
    title: () => t('cols.platform'),
    key: 'platform',
    width: 120,
    render: (row) => platformLabel(row.platform),
  },
  {
    title: () => t('cols.nodeId'),
    key: 'nodeId',
    width: 220,
    render: (row) =>
      h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 11px;' }, row.nodeId),
  },
  {
    title: () => t('cols.capabilities'),
    key: 'capabilities',
    render: (row) =>
      h(
        'div',
        { style: 'display: flex; flex-wrap: wrap; gap: 4px;' },
        (row.capabilities ?? []).map((c: string) =>
          h(NTag, { size: 'tiny', bordered: false }, () => c),
        ),
      ),
  },
  {
    title: () => t('cols.actions'),
    key: 'actions',
    width: 120,
    align: 'right',
    render: (row) =>
      h(
        NPopconfirm,
        { onPositiveClick: () => revoke(row.nodeId) },
        {
          trigger: () =>
            h(
              NButton,
              { size: 'tiny', type: 'error', tertiary: true },
              {
                icon: () => h(TSvgIcon, { icon: 'mdi:link-variant-off', size: 12 }),
                default: () => t('actions.revoke'),
              },
            ),
          default: () => t('revokeConfirm', { name: row.name }),
        },
      ),
  },
]

const pairingColumns: DataTableColumns<PairingRequestDto> = [
  {
    title: () => t('cols.code'),
    key: 'code',
    width: 140,
    render: (row) =>
      h(
        'code',
        { style: 'font-family: var(--tnzi-font-mono); font-size: 16px; font-weight: 600; letter-spacing: 2px;' },
        row.code,
      ),
  },
  {
    title: () => t('cols.deviceName'),
    key: 'deviceName',
    width: 200,
    render: (row) =>
      h('div', { style: 'display: flex; align-items: center; gap: 8px;' }, [
        h(TSvgIcon, { icon: platformIcon(row.platform), size: 16 }),
        h('span', null, row.deviceName),
      ]),
  },
  {
    title: () => t('cols.platform'),
    key: 'platform',
    width: 120,
    render: (row) => platformLabel(row.platform),
  },
  {
    title: () => t('cols.nodeId'),
    key: 'nodeId',
    width: 200,
    render: (row) =>
      h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 11px;' }, row.nodeId),
  },
  {
    title: () => t('cols.createdAt'),
    key: 'createdAt',
    width: 170,
    render: (row) => formatDate(row.createdAt),
  },
  {
    title: () => t('cols.expiresAt'),
    key: 'expiresAt',
    width: 170,
    render: (row) => formatDate(row.expiresAt),
  },
  {
    title: () => t('cols.actions'),
    key: 'actions',
    width: 200,
    align: 'right',
    render: (row) =>
      h('div', { style: 'display: flex; justify-content: flex-end; gap: 6px;' }, [
        h(
          NButton,
          { size: 'tiny', type: 'primary', onClick: () => approve(row.id) },
          {
            icon: () => h(TSvgIcon, { icon: 'mdi:check', size: 12 }),
            default: () => t('actions.approve'),
          },
        ),
        h(
          NPopconfirm,
          { onPositiveClick: () => reject(row.id) },
          {
            trigger: () =>
              h(
                NButton,
                { size: 'tiny', tertiary: true },
                {
                  icon: () => h(TSvgIcon, { icon: 'mdi:close', size: 12 }),
                  default: () => t('actions.reject'),
                },
              ),
            default: () => t('rejectConfirm'),
          },
        ),
      ]),
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [d, p] = await Promise.all([
      bridge.getDevices(),
      bridge.getPendingPairings(),
    ])
    devices.value = d
    pendings.value = p
  } catch {
    devices.value = []
    pendings.value = []
  } finally {
    loading.value = false
  }
}

async function approve(requestId: string): Promise<void> {
  try {
    await bridge.approvePairing(requestId)
    await refresh()
  } catch { /* bridge swallows */ }
}
async function reject(requestId: string): Promise<void> {
  try {
    await bridge.rejectPairing(requestId)
    await refresh()
  } catch { /* bridge swallows */ }
}
async function revoke(nodeId: string): Promise<void> {
  try {
    await bridge.revokeDevice(nodeId)
    await refresh()
  } catch { /* bridge swallows */ }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
/* Layout shell from shared `.t-stack-page` + `.t-table-tabs` utilities. */
.t-devices-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
  padding: 16px 20px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-devices-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-devices-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-devices-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-devices-page__kpis {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}
@media (max-width: 768px) {
  .t-devices-page__kpis { grid-template-columns: 1fr; }
}
</style>
