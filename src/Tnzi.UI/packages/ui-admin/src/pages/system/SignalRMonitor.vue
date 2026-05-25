<template>
  <!--
    SignalRMonitor — surfaces /admin/signalr/{stats,online-users,...}.
    Top: KPI strip (online users, total connections, timestamp). Body: a
    grouped table of online users + their connections (expandable rows
    showing per-connection IP/UA/Hub/groups). One destructive action per
    row (force disconnect) gated by NPopconfirm.
  -->
  <div class="t-signalr-page">
    <div class="t-signalr-page__header">
      <div class="t-signalr-page__title">
        <TSvgIcon icon="mdi:broadcast" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-signalr-page__toolbar">
        <NText depth="3" style="font-size: 12px">
          {{ stats?.timestamp ? t('lastUpdate', { time: formatDate(stats.timestamp) }) : '' }}
        </NText>
        <NButton size="small" :loading="loading" @click="refresh">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
      </div>
    </div>

    <div class="t-signalr-page__kpis">
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.onlineUsers')" :value="stats?.onlineUserCount ?? 0">
          <template #suffix><TSvgIcon icon="mdi:account-group" :size="14" /></template>
        </NStatistic>
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.connections')" :value="stats?.totalConnectionCount ?? 0">
          <template #suffix><TSvgIcon icon="mdi:lan-connect" :size="14" /></template>
        </NStatistic>
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.hubs')" :value="hubCount">
          <template #suffix><TSvgIcon icon="mdi:hub" :size="14" /></template>
        </NStatistic>
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.groups')" :value="groupCount">
          <template #suffix><TSvgIcon icon="mdi:account-group-outline" :size="14" /></template>
        </NStatistic>
      </NCard>
    </div>

    <NCard :title="t('sections.users')" size="small" :bordered="false">
      <template #header-extra>
        <NInput
          v-model:value="filterText"
          :placeholder="t('filter.user')"
          clearable
          size="small"
          style="width: 240px"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        </NInput>
      </template>
      <NDataTable
        :columns="columns"
        :data="filteredUsers"
        :row-key="(row: OnlineUserDto) => row.userId"
        :loading="loading"
        :pagination="{ pageSize: 15 }"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </NCard>
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NInput,
  NPopconfirm,
  NStatistic,
  NTag,
  NText,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import {
  createSignalRBridge,
  type OnlineUserDto,
  type SignalRStatsDto,
} from '../../services/bridges/signalr-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'

const bridge = createSignalRBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('system.signalr', key), params)

const loading = ref(false)
const stats = ref<SignalRStatsDto | null>(null)
const users = ref<OnlineUserDto[]>([])
const filterText = ref('')

// Defensive iteration: when the SignalR module isn't loaded or returns
// a malformed payload (e.g. wrapped envelope without a `data` array),
// `users.value` / `u.connections` can land as non-arrays. Guard every
// loop so the computed never throws "X is not iterable" (which would
// crash the whole page tree, not just the affected card).
const hubCount = computed(() => {
  const set = new Set<string>()
  const list = Array.isArray(users.value) ? users.value : []
  for (const u of list) {
    const conns = Array.isArray(u?.connections) ? u.connections : []
    for (const c of conns) {
      if (c?.hubName) set.add(c.hubName)
    }
  }
  return set.size
})
const groupCount = computed(() => {
  const set = new Set<string>()
  const list = Array.isArray(users.value) ? users.value : []
  for (const u of list) {
    const conns = Array.isArray(u?.connections) ? u.connections : []
    for (const c of conns) {
      const groups = Array.isArray(c?.groups) ? c.groups : []
      for (const g of groups) set.add(g)
    }
  }
  return set.size
})

const filteredUsers = computed(() => {
  const list = Array.isArray(users.value) ? users.value : []
  const q = filterText.value.trim().toLowerCase()
  if (!q) return list
  return list.filter((u) => {
    if (u?.userId?.toLowerCase().includes(q)) return true
    const conns = Array.isArray(u?.connections) ? u.connections : []
    return conns.some(
      (c) =>
        c?.userName?.toLowerCase().includes(q) ||
        c?.ipAddress?.toLowerCase().includes(q) ||
        c?.hubName?.toLowerCase().includes(q),
    )
  })
})

function formatDate(iso: string | null | undefined): string {
  if (!iso) return ''
  try { return new Date(iso).toLocaleString() } catch { return iso }
}

const columns = computed<DataTableColumns<OnlineUserDto>>(() => [
  {
    type: 'expand',
    expandable: () => true,
    renderExpand: (row) =>
      h('div', { class: 't-signalr-page__conns' }, [
        h(
          NDataTable,
          {
            size: 'small',
            bordered: false,
            data: Array.isArray(row?.connections) ? row.connections : [],
            columns: [
              {
                title: () => t('cols.connectionId'),
                key: 'connectionId',
                width: 220,
                render: (c) =>
                  h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 11px;' }, c.connectionId),
              },
              { title: () => t('cols.hub'), key: 'hubName', width: 140 },
              {
                title: () => t('cols.connectedAt'),
                key: 'connectedAt',
                width: 170,
                render: (c) => formatDate(c.connectedAt),
              },
              { title: () => t('cols.ip'), key: 'ipAddress', width: 140 },
              { title: () => t('cols.userAgent'), key: 'userAgent', ellipsis: { tooltip: true } },
              {
                title: () => t('cols.groups'),
                key: 'groups',
                width: 220,
                render: (c) =>
                  h(
                    'div',
                    { style: 'display: flex; flex-wrap: wrap; gap: 4px;' },
                    (c.groups ?? []).map((g: string) =>
                      h(NTag, { size: 'tiny', bordered: false }, () => g),
                    ),
                  ),
              },
            ],
          },
        ),
      ]),
  },
  {
    title: () => t('cols.userId'),
    key: 'userId',
    ellipsis: { tooltip: true },
    render: (row) =>
      h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.userId),
  },
  {
    title: () => t('cols.userName'),
    key: 'userName',
    width: 200,
    render: (row) => {
      const conns = Array.isArray(row?.connections) ? row.connections : []
      return conns.find((c) => c.userName)?.userName ?? '—'
    },
  },
  {
    title: () => t('cols.connectionCount'),
    key: 'connectionCount',
    width: 120,
    align: 'right',
  },
  {
    title: () => t('cols.actions'),
    key: 'actions',
    width: 140,
    align: 'right',
    render: (row) =>
      h(
        NPopconfirm,
        { onPositiveClick: () => disconnect(row.userId) },
        {
          trigger: () =>
            h(
              NButton,
              { size: 'tiny', type: 'warning', tertiary: true },
              {
                icon: () => h(TSvgIcon, { icon: 'mdi:close-circle-outline', size: 12 }),
                default: () => t('actions.disconnect'),
              },
            ),
          default: () => t('disconnectConfirm'),
        },
      ),
  },
])

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [s, list] = await Promise.all([
      bridge.getStats(),
      bridge.getOnlineUsers(),
    ])
    stats.value = s
    // Defensive: bridge `?? []` only covers null/undefined — a 404 with a
    // non-array envelope (e.g. `{ success: false }`) would otherwise land
    // as `{}` and break iteration. Always normalise to a real array.
    users.value = Array.isArray(list) ? list : []
  } catch {
    stats.value = null
    users.value = []
  } finally {
    loading.value = false
  }
}

async function disconnect(userId: string): Promise<void> {
  try {
    await bridge.disconnectUser(userId)
    await refresh()
  } catch { /* bridge swallows */ }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
.t-signalr-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  width: 100%;
  height: 100%;
  min-height: 0;
}
/* SignalR has a single primary table (online users). The list card grows
   to fill the residual height after the KPI strip; the NDataTable inside
   uses `flex-height` (set on the component) so its scrollable body fills
   the residual area and the table's built-in pagination stays anchored
   at the bottom. Naive UI's content wrapper class is `n-card-content`
   (dash, NOT BEM `n-card__content`) — must be a flex column with
   min-height: 0 for `flex-height` to compute correctly. */
.t-signalr-page :deep(.n-card:last-of-type) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-signalr-page :deep(.n-card:last-of-type > .n-card-content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-signalr-page :deep(.n-card:last-of-type .n-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}
.t-signalr-page__header {
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
.t-signalr-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-signalr-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-signalr-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-signalr-page__kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
@media (max-width: 900px) {
  .t-signalr-page__kpis { grid-template-columns: repeat(2, 1fr); }
}
.t-signalr-page__conns {
  padding: 8px 16px 12px;
}
</style>
