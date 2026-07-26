<template>
  <!--
    Performance - wraps /admin/performance/{summary,endpoints,slow-requests}.
    Layout: header (window selector + KPI row) → tabbed table region
    (endpoints | slow-requests). Tabs replaced the stacked two-card
    layout because vertical stacking forced the slow-requests card to
    either eat the viewport or scroll the entire page; with tabs each
    table fills the residual height and gets its own pagination at the
    bottom of the visible card. Read-mostly; one destructive button
    (clear) gated by NPopconfirm.
    -->
  <TTabsPage :title="t('title')" :translate="t" :sections="tabs" default-section="endpoints">
    <template #actions>
      <NSelect
        v-model:value="windowMinutes"
        :options="windowOptions"
        size="small"
        class="w-160px"
        @update:value="refresh"
      />
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
      <NPopconfirm @positive-click="clearAll">
        <template #trigger>
          <NButton size="small" type="warning" tertiary>
            <template #icon><TSvgIcon icon="mdi:delete-sweep-outline" :size="14" /></template>
            {{ t('actions.clear') }}
          </NButton>
        </template>
        {{ t('clearConfirm') }}
      </NPopconfirm>
    </template>

    <!-- Window-level percentile summary, shared across both tabs, so it sits
         above the tab surface (visible on every tab) via the #kpis slot. -->
    <template #kpis>
      <TKpiRow cols="1 s:2 m:5">
        <TKpiCard :label="t('kpi.p50')" :value="formatMs(summary?.p50)" />
        <TKpiCard :label="t('kpi.p95')" :value="formatMs(summary?.p95)" />
        <TKpiCard :label="t('kpi.p99')" :value="formatMs(summary?.p99)" />
        <TKpiCard :label="t('kpi.avg')" :value="formatMs(summary?.average)" />
        <TKpiCard :label="t('kpi.samples')" :value="summary?.sampleCount ?? null" />
      </TKpiRow>
    </template>

    <template #endpoints>
      <div class="t-table-tabs__toolbar">
        <NText depth="3" class="text-12px">
          {{ t('sections.endpointsHint', { minutes: windowMinutes }) }}
        </NText>
      </div>
      <TResponsiveTable
        :columns="endpointColumns"
        :data="endpoints"
        :loading="loading"
        :pagination="{ pageSize: 15 }"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </template>

    <template #slow>
      <div class="t-table-tabs__toolbar">
        <NText depth="3" class="text-12px">
          {{ t('sections.slowHint') }}
        </NText>
      </div>
      <TResponsiveTable
        :columns="slowColumns"
        :data="slow"
        :loading="loading"
        :pagination="{ pageSize: 10 }"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { h, onMounted, ref } from 'vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { TKpiCard, TKpiRow } from '../../components/data'
import {
  NButton,
  NPopconfirm,
  NSelect,
  NTag,
  NText,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createPerformanceBridge,
  type EndpointStatsDto,
  type PercentileResultDto,
  type SlowRequestRecordDto,
} from '../../services/bridges/performance-bridge'
import { makePageTranslator } from '../_shared/translate'
import { methodTone } from '../_shared/http-method'
import TTabsPage, { type TabSection } from '../../components/layout/TTabsPage.vue'

const bridge = createPerformanceBridge({ client: useAdminClient() })

const t = makePageTranslator('system.performance')

const loading = ref(false)
const summary = ref<PercentileResultDto | null>(null)
const endpoints = ref<EndpointStatsDto[]>([])
const slow = ref<SlowRequestRecordDto[]>([])
const windowMinutes = ref(60)
// Primary tabs. TTabsPage owns the `?section=` deep-linking + Back/Forward.
// Each pane is a single flex-height table (with a hint toolbar), so no pane
// owns its own scroll.
const tabs: TabSection[] = [
  { name: 'endpoints', label: t('sections.endpoints') },
  { name: 'slow', label: t('sections.slow') },
]
const windowOptions = [
  { value: 15, label: '15 min' },
  { value: 60, label: '1 h' },
  { value: 360, label: '6 h' },
  { value: 1440, label: '24 h' },
]

function formatMs(v: number | undefined | null): string {
  if (v == null) return EMPTY_DASH
  if (v >= 1000) return `${(v / 1000).toFixed(2)} s`
  return `${v.toFixed(1)} ms`
}

const endpointColumns: DataTableColumns<EndpointStatsDto> = [
  {
    title: () => t('cols.method'),
    key: 'method',
    width: 90,
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: methodTone(row.method) }, () => row.method),
  },
  {
    title: () => t('cols.path'),
    key: 'path',
    ellipsis: { tooltip: true },
    render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.path),
  },
  {
    title: () => t('cols.count'),
    key: 'requestCount',
    width: 90,
    align: 'right',
    sorter: (a, b) => a.requestCount - b.requestCount,
  },
  {
    title: () => t('cols.avg'),
    key: 'averageDurationMs',
    width: 100,
    align: 'right',
    sorter: (a, b) => a.averageDurationMs - b.averageDurationMs,
    render: (row) => formatMs(row.averageDurationMs),
  },
  {
    title: () => t('cols.p95'),
    key: 'p95DurationMs',
    width: 100,
    align: 'right',
    sorter: (a, b) => a.p95DurationMs - b.p95DurationMs,
    render: (row) => formatMs(row.p95DurationMs),
  },
  {
    title: () => t('cols.max'),
    key: 'maxDurationMs',
    width: 100,
    align: 'right',
    render: (row) => formatMs(row.maxDurationMs),
  },
  {
    title: () => t('cols.errors'),
    key: 'errorCount',
    width: 90,
    align: 'right',
    render: (row) =>
      row.errorCount > 0
        ? h(NTag, { size: 'tiny', bordered: false, type: 'error' }, () => String(row.errorCount))
        : '0',
  },
  {
    title: () => t('cols.lastSeen'),
    key: 'lastRequestTime',
    width: 170,
    render: (row) => formatDate(row.lastRequestTime),
  },
]

const slowColumns: DataTableColumns<SlowRequestRecordDto> = [
  {
    title: () => t('cols.timestamp'),
    key: 'timestamp',
    width: 170,
    render: (row) => formatDate(row.timestamp),
  },
  {
    title: () => t('cols.method'),
    key: 'method',
    width: 90,
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: methodTone(row.method) }, () => row.method),
  },
  {
    title: () => t('cols.path'),
    key: 'path',
    ellipsis: { tooltip: true },
    render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.path),
  },
  {
    title: () => t('cols.status'),
    key: 'statusCode',
    width: 90,
    align: 'center',
    render: (row) =>
      h(
        NTag,
        {
          size: 'tiny',
          bordered: false,
          type: row.statusCode >= 500 ? 'error' : row.statusCode >= 400 ? 'warning' : 'success',
        },
        () => String(row.statusCode),
      ),
  },
  {
    title: () => t('cols.duration'),
    key: 'durationMs',
    width: 110,
    align: 'right',
    sorter: (a, b) => a.durationMs - b.durationMs,
    render: (row) => formatMs(row.durationMs),
  },
  {
    title: () => t('cols.userId'),
    key: 'userId',
    width: 200,
    ellipsis: { tooltip: true },
    render: (row) => row.userId ?? EMPTY_DASH,
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [s, eps, sl] = await Promise.all([
      bridge.getSummary(windowMinutes.value),
      bridge.getEndpoints(windowMinutes.value, 0),
      bridge.getSlowRequests(50),
    ])
    summary.value = s
    // Defensive: NDataTable's `createTreeMate` throws `rawNodes.forEach is
    // not a function` when handed a non-array (e.g. empty response wrapped
    // as `{}` or `null`). The bridge already falls back to `[]` for
    // null/undefined, but envelope shapes that bypass `?? []` would still
    // explode here without an explicit isArray gate.
    endpoints.value = Array.isArray(eps) ? eps : []
    slow.value = Array.isArray(sl) ? sl : []
  } catch {
    summary.value = null
    endpoints.value = []
    slow.value = []
  } finally {
    loading.value = false
  }
}

async function clearAll(): Promise<void> {
  try {
    await bridge.clear()
    await refresh()
  } catch { /* bridge swallows */ }
}

onMounted(() => {
  void refresh()
})
</script>

