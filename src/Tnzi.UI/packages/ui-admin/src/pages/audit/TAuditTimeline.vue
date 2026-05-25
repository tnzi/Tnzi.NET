<template>
  <div class="t-audit-timeline t-page-scroll">
    <!-- Filters -->
    <NCard size="small" :bordered="false" class="t-audit-timeline__filters">
      <div class="t-audit-timeline__filter-grid">
        <NInput v-model:value="filters.userName" :placeholder="t('filters.userName')" clearable size="small" />
        <NInput v-model:value="filters.functionName" :placeholder="t('filters.functionName')" clearable size="small" />
        <NSelect
          v-model:value="filters.resultType"
          :options="resultOptions"
          :placeholder="t('filters.resultType')"
          clearable
          size="small"
        />
        <NDatePicker
          v-model:value="dateRange"
          type="daterange"
          clearable
          size="small"
          :placeholder="t('filters.dateRange')"
        />
        <NButton type="primary" size="small" :loading="loading" @click="loadFirst">
          {{ t('filters.search') }}
        </NButton>
      </div>
    </NCard>

    <!-- Timeline grouped by day -->
    <NCard size="small" :bordered="false" class="t-audit-timeline__card">
      <NSpin :show="loading && groups.length === 0">
        <div v-if="!groups.length && !loading" class="t-audit-timeline__empty">
          {{ t('empty') }}
        </div>
        <div v-for="group in groups" :key="group.date" class="t-audit-timeline__day">
          <h4 class="t-audit-timeline__day-title">{{ group.date }}</h4>
          <NTimeline size="medium">
            <NTimelineItem
              v-for="item in group.items"
              :key="item.id ?? `${item.userId}-${item.startTime}`"
              :type="typeFor(item)"
              :time="formatTime(item.startTime)"
              line-type="dashed"
            >
              <template #header>
                <div class="t-audit-timeline__item-header">
                  <strong>{{ item.userName ?? item.userId ?? t('anonymous') }}</strong>
                  <span class="t-audit-timeline__action">{{ item.functionName ?? item.url ?? '—' }}</span>
                  <NTag size="small" :bordered="false" :type="typeFor(item)">{{ resultLabel(item.resultType) }}</NTag>
                  <span v-if="item.elapsed != null" class="t-audit-timeline__elapsed">{{ item.elapsed }}ms</span>
                </div>
              </template>
              <div class="t-audit-timeline__item-body" @click="openDetail(item)">
                <code v-if="item.httpMethod || item.url" class="t-audit-timeline__route">
                  {{ item.httpMethod }} {{ item.url }}
                </code>
                <span v-if="item.ip" class="t-audit-timeline__ip">{{ item.ip }}</span>
              </div>
            </NTimelineItem>
          </NTimeline>
        </div>
        <div v-if="hasMore && groups.length" class="t-audit-timeline__more">
          <NButton :loading="loading" @click="loadMore">{{ t('loadMore') }}</NButton>
        </div>
      </NSpin>
    </NCard>

    <!-- Detail drawer -->
    <NDrawer v-model:show="detailOpen" :width="520" placement="right">
      <NDrawerContent :title="t('detail.title')" closable>
        <template v-if="detailItem">
          <NDescriptions :column="1" label-placement="left" size="small" bordered>
            <NDescriptionsItem :label="t('detail.user')">
              {{ detailItem.userName ?? detailItem.userId ?? '—' }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.action')">
              {{ detailItem.functionName ?? '—' }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.url')">
              <code>{{ detailItem.httpMethod }} {{ detailItem.url }}</code>
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.ip')">
              {{ detailItem.ip ?? '—' }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.result')">
              <NTag size="small" :type="typeFor(detailItem)">{{ resultLabel(detailItem.resultType) }}</NTag>
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.elapsed')">
              {{ detailItem.elapsed != null ? `${detailItem.elapsed} ms` : '—' }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.time')">
              {{ formatTime(detailItem.startTime, true) }}
            </NDescriptionsItem>
          </NDescriptions>

          <details v-if="detailItem.message" class="t-audit-timeline__details">
            <summary>{{ t('detail.message') }}</summary>
            <pre>{{ detailItem.message }}</pre>
          </details>
          <details v-if="detailItem.exception" class="t-audit-timeline__details">
            <summary>{{ t('detail.exception') }}</summary>
            <pre>{{ detailItem.exception }}</pre>
          </details>
          <details v-if="detailItem.requestParameters" class="t-audit-timeline__details">
            <summary>{{ t('detail.requestParameters') }}</summary>
            <pre>{{ detailItem.requestParameters }}</pre>
          </details>
        </template>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import {
  NCard, NInput, NSelect, NDatePicker, NButton, NSpin, NTimeline, NTimelineItem,
  NTag, NDrawer, NDrawerContent, NDescriptions, NDescriptionsItem,
} from 'naive-ui'
import { useSafeMessage } from '../_shared/safeMessage'
// 0.2.72+ (B4): Re-routed through the bridge so the page stays clean
// under the `no-restricted-imports` guard against direct
// `@tnzi/core/services/*` value imports from `pages/**`.
import type { AuditOperationDto } from '../../services/bridges/audit-bridge'
import { AuditResultType } from '../../services/bridges/audit-bridge'
import type { CrudPageQuery, CrudPageResult } from '../../services/types'

interface Props {
  /** Title shown in the parent card / page title; passed through translate(). */
  pageId: string
  /** Bridge fetcher — returns CrudPageResult so we can drive pagination. */
  fetch: (query: CrudPageQuery) => Promise<CrudPageResult<AuditOperationDto>>
  /** translate helper from the parent page. */
  translate: (key: string) => string
}

const props = defineProps<Props>()
const t = props.translate

const message = useSafeMessage()

interface Filters {
  userName?: string
  functionName?: string
  resultType?: string
}
const filters = ref<Filters>({})
const dateRange = ref<[number, number] | null>(null)
const pageSize = 30
const pageIndex = ref(1)
const items = ref<AuditOperationDto[]>([])
const total = ref(0)
const loading = ref(false)

const resultOptions = computed(() => [
  { label: t('results.success'), value: AuditResultType.Success },
  { label: t('results.failed'), value: AuditResultType.Failed },
  { label: t('results.warning'), value: AuditResultType.Warning },
])

function resultLabel(rt: AuditResultType | undefined | null): string {
  if (rt === AuditResultType.Success) return t('results.success')
  if (rt === AuditResultType.Failed) return t('results.failed')
  if (rt === AuditResultType.Warning) return t('results.warning')
  return '—'
}

const hasMore = computed(() => items.value.length < total.value)

interface DayGroup {
  date: string
  items: AuditOperationDto[]
}
const groups = computed<DayGroup[]>(() => {
  const out = new Map<string, AuditOperationDto[]>()
  for (const it of items.value) {
    const day = formatDay(it.startTime)
    if (!out.has(day)) out.set(day, [])
    out.get(day)!.push(it)
  }
  return [...out.entries()].map(([date, items]) => ({ date, items }))
})

function formatDay(v?: string | Date | null): string {
  if (!v) return '—'
  try {
    const d = new Date(v)
    const y = d.getFullYear()
    const m = String(d.getMonth() + 1).padStart(2, '0')
    const day = String(d.getDate()).padStart(2, '0')
    return `${y}-${m}-${day}`
  } catch {
    return '—'
  }
}

function formatTime(v?: string | Date | null, withDate = false): string {
  if (!v) return ''
  try {
    const d = new Date(v)
    const hh = String(d.getHours()).padStart(2, '0')
    const mm = String(d.getMinutes()).padStart(2, '0')
    const ss = String(d.getSeconds()).padStart(2, '0')
    if (!withDate) return `${hh}:${mm}:${ss}`
    return `${formatDay(d)} ${hh}:${mm}:${ss}`
  } catch {
    return ''
  }
}

function typeFor(item: AuditOperationDto): 'success' | 'error' | 'default' | 'warning' | 'info' {
  if (item.resultType === AuditResultType.Success) return 'success'
  if (item.resultType === AuditResultType.Failed) return 'error'
  if (item.resultType === AuditResultType.Warning) return 'warning'
  return 'info'
}

function buildQuery(): CrudPageQuery {
  const filt: Record<string, unknown> = { ...filters.value }
  if (dateRange.value) {
    filt.startTimeFrom = new Date(dateRange.value[0]).toISOString()
    filt.startTimeTo = new Date(dateRange.value[1]).toISOString()
  }
  return {
    pageIndex: pageIndex.value,
    pageSize,
    sortField: 'startTime',
    sortOrder: 'desc' as const,
    searchText: '',
    filters: filt,
  }
}

async function loadFirst(): Promise<void> {
  pageIndex.value = 1
  items.value = []
  total.value = 0
  await loadInternal()
}

async function loadMore(): Promise<void> {
  pageIndex.value += 1
  await loadInternal(true)
}

// Monotonic token guarding stale responses — repeated Search-button clicks
// and back-to-back Load-more taps must not let an older page overwrite a
// newer one (especially since the filter row sits above the list).
let fetchToken = 0

async function loadInternal(append = false): Promise<void> {
  const myToken = ++fetchToken
  loading.value = true
  try {
    const result = await props.fetch(buildQuery())
    if (myToken !== fetchToken) return
    items.value = append ? [...items.value, ...result.items] : result.items
    total.value = result.totalCount
  } catch (e) {
    if (myToken !== fetchToken) return
    message.error(e instanceof Error ? e.message : String(e))
    if (!append) items.value = []
  } finally {
    if (myToken === fetchToken) loading.value = false
  }
}

const detailOpen = ref(false)
const detailItem = ref<AuditOperationDto | null>(null)
function openDetail(item: AuditOperationDto): void {
  detailItem.value = item
  detailOpen.value = true
}

onMounted(() => {
  void loadFirst()
})
</script>

<style scoped>
.t-audit-timeline {
  padding: 16px;
}
.t-audit-timeline__filters {
  margin-bottom: 16px;
}
.t-audit-timeline__filter-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr) auto;
  gap: 8px;
  align-items: center;
}
@media (max-width: 960px) {
  .t-audit-timeline__filter-grid {
    grid-template-columns: 1fr 1fr;
  }
}
.t-audit-timeline__day {
  margin-bottom: 24px;
}
.t-audit-timeline__day-title {
  margin: 0 0 12px;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
  border-bottom: 1px solid var(--tnzi-border);
  padding-bottom: 6px;
}
.t-audit-timeline__item-header {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.t-audit-timeline__action {
  color: var(--tnzi-base-text-muted);
}
.t-audit-timeline__elapsed {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-audit-timeline__item-body {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  margin-top: 4px;
  display: flex;
  gap: 12px;
  align-items: baseline;
  cursor: pointer;
}
.t-audit-timeline__item-body:hover {
  color: var(--tnzi-primary);
}
.t-audit-timeline__route {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  word-break: break-all;
}
.t-audit-timeline__ip {
  white-space: nowrap;
}
.t-audit-timeline__more {
  text-align: center;
  margin-top: 16px;
}
.t-audit-timeline__empty {
  text-align: center;
  color: var(--tnzi-base-text-muted);
  padding: 60px 0;
}
.t-audit-timeline__details {
  margin-top: 12px;
}
.t-audit-timeline__details summary {
  cursor: pointer;
  color: var(--tnzi-primary);
  font-size: 13px;
  margin-bottom: 4px;
}
.t-audit-timeline__details pre {
  background: var(--tnzi-layout-bg);
  padding: 8px;
  border-radius: 4px;
  font-size: 12px;
  overflow: auto;
  max-height: 240px;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
