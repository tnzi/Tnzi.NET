<template>
  <!--
    HealthChecks — read-only viewer for `Tnzi.HealthChecks` JSON output.
    Tnzi.HealthChecks doesn't ship a controller (it uses `MapHealthChecks`
    directly), so this page hits the configured endpoint (`/health/ready` by
    default) which sits outside the `/api` prefix. When the module isn't
    loaded the page renders an "unavailable" banner explaining how to wire it.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NSelect
        v-model:value="endpoint"
        :options="endpointOptions"
        size="small"
        class="w-200px"
        @update:value="refresh"
      />
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <NCard size="small" :bordered="false" class="t-hc-page__overall">
      <div class="t-hc-page__overall-row">
        <div class="t-hc-page__overall-left">
          <NTag :type="overallTone" size="large" :bordered="false">
            <template #icon>
              <TSvgIcon :icon="overallIcon" :size="14" />
            </template>
            {{ overallLabel }}
          </NTag>
          <div class="t-hc-page__overall-meta">
            <div>
              <span class="t-hc-page__meta-label">{{ t('overall.endpoint') }}:</span>
              <code class="t-hc-page__meta-value">{{ endpoint }}</code>
            </div>
            <div v-if="report">
              <span class="t-hc-page__meta-label">{{ t('overall.duration') }}:</span>
              <span class="t-hc-page__meta-value">{{ formatMs(report.totalDuration) }}</span>
            </div>
            <div v-if="lastUpdate">
              <span class="t-hc-page__meta-label">{{ t('overall.updated') }}:</span>
              <span class="t-hc-page__meta-value">{{ formatDate(lastUpdate) }}</span>
            </div>
          </div>
        </div>
      </div>
    </NCard>

    <NAlert v-if="unavailable" :title="t('unavailable.title')" type="warning" :closable="false">
      <div>{{ t('unavailable.body') }}</div>
      <div class="mt-8px">
        <code class="tnzi-mono text-12px">
          [DependsOn(typeof(HealthChecksModule))]
        </code>
      </div>
    </NAlert>

    <NCard v-else :title="t('sections.checks')" size="small" :bordered="false" class="t-table-card">
      <TResponsiveTable
        :columns="columns"
        :data="entries"
        :loading="loading"
        :pagination="false"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NSelect,
  NTag,
} from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import { interpolate, translatePageKey } from '../_shared/translate'
import TContentPage from '../../components/layout/TContentPage.vue'

const client = useAdminClient()
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('system.healthchecks', key), params)

interface HealthEntry {
  name: string
  status: string
  duration: number
  description?: string | null
  data?: Record<string, unknown> | null
  exception?: string | null
}

interface HealthReport {
  status: string
  totalDuration: number
  entries: HealthEntry[]
}

const loading = ref(false)
const endpoint = ref('/health/ready')
const endpointOptions = [
  { value: '/health/ready', label: '/health/ready' },
  { value: '/health', label: '/health' },
  { value: '/health/live', label: '/health/live' },
]
const report = ref<HealthReport | null>(null)
const unavailable = ref(false)
const lastUpdate = ref<string | null>(null)

const entries = computed(() => report.value?.entries ?? [])

const overallTone = computed<'success' | 'warning' | 'error' | 'default'>(() => {
  if (unavailable.value) return 'default'
  const s = report.value?.status?.toLowerCase()
  if (s === 'healthy') return 'success'
  if (s === 'degraded') return 'warning'
  if (s === 'unhealthy') return 'error'
  return 'default'
})
const overallIcon = computed(() => {
  if (unavailable.value) return 'mdi:cloud-off-outline'
  const s = report.value?.status?.toLowerCase()
  if (s === 'healthy') return 'mdi:check-circle'
  if (s === 'degraded') return 'mdi:alert'
  if (s === 'unhealthy') return 'mdi:alert-circle'
  return 'mdi:help-circle-outline'
})
const overallLabel = computed(() => {
  if (unavailable.value) return t('overall.unavailable')
  return report.value?.status ?? t('overall.unknown')
})

function entryTone(s: string): 'success' | 'warning' | 'error' | 'default' {
  switch ((s ?? '').toLowerCase()) {
    case 'healthy': return 'success'
    case 'degraded': return 'warning'
    case 'unhealthy': return 'error'
    default: return 'default'
  }
}

function formatMs(v: number | undefined | null): string {
  if (v == null) return '—'
  if (v >= 1000) return `${(v / 1000).toFixed(2)} s`
  return `${v.toFixed(1)} ms`
}

const columns: DataTableColumns<HealthEntry> = [
  {
    title: () => t('cols.status'),
    key: 'status',
    width: 130,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: entryTone(row.status) }, () => row.status),
  },
  { title: () => t('cols.name'), key: 'name', width: 220 },
  {
    title: () => t('cols.duration'),
    key: 'duration',
    width: 110,
    align: 'right',
    render: (row) => formatMs(row.duration),
  },
  {
    title: () => t('cols.description'),
    key: 'description',
    ellipsis: { tooltip: true },
    render: (row) => row.description ?? row.exception ?? '—',
  },
]

async function refresh(): Promise<void> {
  loading.value = true
  unavailable.value = false
  try {
    // `/health*` lives outside the `/api` prefix, so HttpClient's
    // baseUrl + ApiResult unwrap pipeline doesn't apply. We use raw
    // fetch but forward the admin bearer token in case the consumer
    // protects /health behind auth (Tnzi.HealthChecks defaults to
    // anonymous, but a deployment can wrap MapHealthChecks with
    // .RequireAuthorization()). `credentials: 'include'` covers cookie
    // auth. For cross-origin deployments (admin on a different host than
    // the API) consumers must either proxy /health* in their dev/prod
    // gateway (see Acme's vite.config.ts) or override the endpoint
    // via the route prop.
    const token = client.getAccessToken?.() ?? null
    const headers: Record<string, string> = { Accept: 'application/json' }
    if (token) headers.Authorization = `Bearer ${token}`
    const res = await fetch(endpoint.value, { credentials: 'include', headers })
    if (res.status === 404) {
      unavailable.value = true
      report.value = null
    } else {
      const text = await res.text()
      try {
        const parsed = JSON.parse(text) as HealthReport
        report.value = parsed
      } catch {
        // Plain "Healthy" / "Unhealthy" body (no DetailedOutput).
        report.value = { status: text.trim() || 'Unknown', totalDuration: 0, entries: [] }
      }
    }
    lastUpdate.value = new Date().toISOString()
  } catch {
    unavailable.value = true
    report.value = null
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
/* Layout shell from TContentPage (scroll="fill") + shared `.t-table-card` utilities. */
.t-hc-page__overall-row {
  display: flex;
  align-items: center;
  gap: 16px;
}
.t-hc-page__overall-left {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}
.t-hc-page__overall-meta {
  display: flex;
  align-items: center;
  gap: 24px;
  font-size: 13px;
  flex-wrap: wrap;
}
.t-hc-page__meta-label {
  color: var(--tnzi-base-text-muted, #888);
  margin-right: 4px;
}
.t-hc-page__meta-value {
  font-weight: 500;
}
</style>
