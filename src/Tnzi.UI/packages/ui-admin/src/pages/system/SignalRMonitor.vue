<template>
  <!--
    SignalRMonitor — surfaces /admin/signalr/{stats,online-users,...}.
    Top: KPI strip (online users, total connections, timestamp). Body: a
    grouped table of online users + their connections (expandable rows
    showing per-connection IP/UA/Hub/groups). One destructive action per
    row (force disconnect) declared via TResponsiveTable `:row-actions`
    (confirm gated).
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NText depth="3" class="text-12px">
        {{ stats?.timestamp ? t('lastUpdate', { time: formatDate(stats.timestamp) }) : '' }}
      </NText>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <TKpiRow cols="1 s:2 m:4" class="t-signalr-page__kpis">
      <TKpiCard :label="t('kpi.onlineUsers')" :value="stats?.onlineUserCount ?? 0" icon="mdi:account-group" />
      <TKpiCard :label="t('kpi.connections')" :value="stats?.totalConnectionCount ?? 0" icon="mdi:lan-connect" />
      <TKpiCard :label="t('kpi.hubs')" :value="hubCount" icon="mdi:hub" />
      <TKpiCard :label="t('kpi.groups')" :value="groupCount" icon="mdi:account-group-outline" />
    </TKpiRow>

    <NCard :title="t('sections.users')" size="small" :bordered="false" class="t-signalr-page__list-card">
      <template #header-extra>
        <NInput
          v-model:value="filterText"
          :placeholder="t('filter.user')"
          clearable
          size="small"
          class="w-240px"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        </NInput>
      </template>
      <!-- Online users have expandable rows (a nested per-connection table),
           so on phones we keep the table form with horizontal scroll rather
           than collapsing to cards (which would drop the expand affordance). -->
      <TResponsiveTable
        mobile="scroll"
        :columns="columns"
        :data="filteredUsers"
        :row-key="(row: OnlineUserDto) => row.userId"
        :loading="loading"
        :pagination="{ pageSize: 15 }"
        :row-actions="rowActions"
        :row-actions-title="t('cols.actions')"
        :translate="t"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { type RowAction } from '../../headless/rowActions'
import {
  NButton,
  NCard,
  NDataTable,
  NInput,
  NTag,
  NText,
} from 'naive-ui'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createSignalRBridge,
  type OnlineUserDto,
  type SignalRStatsDto,
} from '../../services/bridges/signalr-bridge'
import { makePageTranslator } from '../_shared/translate'
import TContentPage from '../../components/layout/TContentPage.vue'
import { usePermissionGuard } from '../../headless/usePermissionGuard'

const bridge = createSignalRBridge({ client: useAdminClient() })
const t = makePageTranslator('system.signalr')

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
                  h('code', { class: 'tnzi-mono text-11px' }, c.connectionId),
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
                    { class: 'flex flex-wrap gap-4px' },
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
      h('code', { class: 'tnzi-mono text-12px' }, row.userId),
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
])

// Declarative operation column — force-disconnect every connection for a user
// (confirm gated) via the existing disconnect handler.
// 按钮级权限门控(fail-open,后端 [ApiAuthorize system.signalr.execute] 是真墙)
const { can } = usePermissionGuard()
const rowActions: RowAction<OnlineUserDto>[] = [
  {
    key: 'disconnect',
    label: 'actions.disconnect',
    icon: 'mdi:close-circle-outline',
    type: 'warning',
    confirm: 'disconnectConfirm',
    show: () => can('system.signalr.execute'),
    onClick: (row) => void disconnect(row.userId),
  },
]

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
/* SignalR has a single primary table (online users). The list card grows
   to fill the residual height after the KPI strip; the NDataTable inside
   uses `flex-height` (set on the component) so its scrollable body fills
   the residual area and the table's built-in pagination stays anchored
   at the bottom. Naive UI's content wrapper class is `n-card-content`
   (dash, NOT BEM `n-card__content`) — must be a flex column with
   min-height: 0 for `flex-height` to compute correctly. Targeted by an
   explicit class so the TKpiCard NCards in the KPI strip stay untouched. */
:deep(.t-signalr-page__list-card) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
:deep(.t-signalr-page__list-card > .n-card-content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
:deep(.t-signalr-page__list-card .n-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}
.t-signalr-page__kpis {
  flex-shrink: 0;
}
.t-signalr-page__conns {
  padding: 8px 16px 12px;
}
</style>
