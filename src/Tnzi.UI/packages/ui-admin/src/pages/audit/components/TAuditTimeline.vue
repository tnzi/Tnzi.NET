<template>
  <div class="t-audit-timeline">
    <!-- Filter toolbar — flush on the parent card surface (Logs/Operations
         wrap this in a `TContentPage card scroll="fill"`, so the white surface
         and fill-height come from the shell; this component just fills it). -->
    <div class="t-audit-timeline__filters">
      <TUserSelector
        :value="filters.userId ?? null"
        :fetcher="userFetcher"
        :placeholder="t('filters.userName')"
        size="small"
        class="!w-190px max-w-full"
        @update:value="onUserFilterChange"
      />
      <NInput
        v-model:value="filters.functionName"
        :placeholder="t('filters.functionName')"
        clearable
        size="small"
        class="!w-190px max-w-full"
        @keyup.enter="loadFirst"
      />
      <NSelect
        v-model:value="filters.resultType"
        :options="resultOptions"
        :placeholder="t('filters.resultType')"
        clearable
        size="small"
        class="!w-150px max-w-full"
      />
      <NDatePicker
        v-model:value="dateRange"
        type="daterange"
        clearable
        size="small"
        class="!w-260px max-w-full"
      />
      <NButton type="primary" size="small" :loading="loading" @click="loadFirst">
        <template #icon><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        {{ t('filters.search') }}
      </NButton>
      <NButton v-if="hasActiveFilter" size="small" tertiary @click="resetFilters">
        {{ t('filters.reset') }}
      </NButton>
      <span class="t-audit-timeline__count">{{ t('summary', { n: total }) }}</span>
    </div>

    <!-- Timeline grouped by day — scrolls inside the filled card -->
    <div class="t-audit-timeline__scroll">
      <NSpin :show="loading && groups.length === 0">
        <TEmpty
          v-if="!groups.length && !loading"
          :text="t('empty')"
          icon="mdi:clipboard-text-clock-outline"
        />

        <div v-for="group in groups" :key="group.date" class="t-audit-timeline__day">
          <div class="t-audit-timeline__day-title">
            <TSvgIcon icon="mdi:calendar-blank-outline" :size="13" />
            <span>{{ group.date }}</span>
            <span class="t-audit-timeline__day-count">{{ group.items.length }}</span>
          </div>
          <NTimeline size="medium">
            <NTimelineItem
              v-for="item in group.items"
              :key="item.id ?? `${item.userId}-${item.startTime}`"
              :type="typeFor(item)"
              :time="timeLabel(item.startTime)"
              line-type="dashed"
            >
              <div
                class="t-audit-timeline__item"
                role="button"
                tabindex="0"
                @click="openDetail(item)"
                @keydown.enter="openDetail(item)"
              >
                <div class="t-audit-timeline__item-main">
                  <strong class="t-audit-timeline__user">{{ item.userName ?? item.userId ?? t('anonymous') }}</strong>
                  <span class="t-audit-timeline__action">{{ item.functionName ?? item.url ?? '—' }}</span>
                  <NTag size="small" :bordered="false" :type="typeFor(item)">{{ resultLabel(item.resultType) }}</NTag>
                  <span v-if="item.elapsed != null" class="t-audit-timeline__elapsed">{{ item.elapsed }}ms</span>
                  <TSvgIcon class="t-audit-timeline__chevron" icon="mdi:chevron-right" :size="16" />
                </div>
                <div v-if="item.httpMethod || item.url || item.ip" class="t-audit-timeline__item-sub">
                  <code v-if="item.httpMethod || item.url" class="t-audit-timeline__route">
                    {{ item.httpMethod }} {{ item.url }}
                  </code>
                  <span v-if="item.ip" class="t-audit-timeline__ip">{{ item.ip }}</span>
                </div>
              </div>
            </NTimelineItem>
          </NTimeline>
        </div>

        <div v-if="hasMore && groups.length" class="t-audit-timeline__more">
          <NButton size="small" :loading="loading" @click="loadMore">{{ t('loadMore') }}</NButton>
        </div>
      </NSpin>
    </div>

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
              {{ formatDateTime(detailItem.startTime, { fallback: '—' }) }}
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
  NButton, NInput, NSelect, NDatePicker, NSpin, NTimeline, NTimelineItem,
  NTag, NDrawer, NDrawerContent, NDescriptions, NDescriptionsItem,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDate, formatDateOnly, formatDateTime } from '@tnzi/core'
import TEmpty from '../../../components/data/TEmpty.vue'
import TUserSelector from '../../../components/forms/TUserSelector.vue'
import type { SelectorOption } from '../../../components/forms/_selector-factory'
import { useSafeMessage } from '../../_shared/safeMessage'
// 0.2.72+ (B4): Re-routed through the bridge so the page stays clean
// under the `no-restricted-imports` guard against direct
// `@tnzi/core/services/*` value imports from `pages/**`.
import type { AuditOperationDto } from '../../../services/bridges/audit-bridge'
import { AuditResultType } from '../../../services/bridges/audit-bridge'
import { createIdentityBridge } from '../../../services/bridges/identity-bridge'
import { useAdminClient } from '../../../plugin/client'
import type { CrudPageQuery, CrudPageResult } from '../../../services/types'

interface Props {
  /** Title shown in the parent card / page title; passed through translate(). */
  pageId: string
  /** Bridge fetcher — returns CrudPageResult so we can drive pagination. */
  fetch: (query: CrudPageQuery) => Promise<CrudPageResult<AuditOperationDto>>
  /** translate helper from the parent page (interpolation-aware). */
  translate: (key: string, params?: Record<string, unknown>) => string
}

const props = defineProps<Props>()
const t = props.translate

const message = useSafeMessage()

// Identity bridge powers the user-filter selector (remote keyword search →
// userId). Kept local to the component so both Logs/Operations reuse it.
const idBridge = createIdentityBridge({ client: useAdminClient() })

const userFetcher = async (keyword: string): Promise<SelectorOption[]> => {
  try {
    const res = await idBridge.users.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: keyword.trim(),
      sortField: undefined,
      sortOrder: null,
      filters: {},
    })
    return res.items.map((u) => ({
      label: u.email ? `${u.userName} (${u.email})` : u.userName,
      value: u.id,
    }))
  } catch {
    // A failed lookup should not spam the message center — the selector just
    // shows no options. The audit list itself surfaces its own errors.
    return []
  }
}

interface Filters {
  /** Backend `AuditOperationQueryDto.UserId` (Guid) — set from the selector. */
  userId?: string
  functionName?: string
  resultType?: AuditResultType
}
const filters = ref<Filters>({})
const dateRange = ref<[number, number] | null>(null)
const pageSize = 30
const pageIndex = ref(1)
const items = ref<AuditOperationDto[]>([])
const total = ref(0)
const loading = ref(false)

// Result type is wired as a PascalCase member-name string (backend global
// JsonStringEnumConverter). AuditResultType is a string-valued enum so the
// option values and comparisons below match the response strings directly.
const resultOptions = computed(() => [
  { label: t('results.success'), value: AuditResultType.Success },
  { label: t('results.failed'), value: AuditResultType.Failed },
  { label: t('results.warning'), value: AuditResultType.Warning },
])

const hasActiveFilter = computed(
  () =>
    Boolean(filters.value.userId) ||
    Boolean(filters.value.functionName) ||
    filters.value.resultType != null ||
    dateRange.value != null,
)

function onUserFilterChange(v: unknown): void {
  filters.value.userId = (v as string | null) ?? undefined
}

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
    const day = dayLabel(it.startTime)
    if (!out.has(day)) out.set(day, [])
    out.get(day)!.push(it)
  }
  return [...out.entries()].map(([date, items]) => ({ date, items }))
})

// startTime is a real timestamp (not a date-only field), so render the local
// calendar date / time — no `utc` normalization.
function dayLabel(v?: string | Date | null): string {
  return formatDateOnly(v, { fallback: '—' })
}

function timeLabel(v?: string | Date | null): string {
  if (!v) return ''
  return formatDate(v, 'HH:mm:ss')
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
    // Backend `AuditOperationQueryDto.StartDate` / `EndDate`.
    filt.startDate = new Date(dateRange.value[0]).toISOString()
    filt.endDate = new Date(dateRange.value[1]).toISOString()
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

function resetFilters(): void {
  filters.value = {}
  dateRange.value = null
  void loadFirst()
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
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

/* Filter toolbar — flush, wraps gracefully; the count is pushed to the far
   right of the first row (or wraps below on narrow widths). */
.t-audit-timeline__filters {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  flex-shrink: 0;
  padding-bottom: 12px;
  margin-bottom: 12px;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
.t-audit-timeline__count {
  margin-left: auto;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  white-space: nowrap;
}

/* Scroll region claims the residual height and scrolls internally. */
.t-audit-timeline__scroll {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}

.t-audit-timeline__day {
  margin-bottom: 20px;
}
.t-audit-timeline__day:last-child {
  margin-bottom: 0;
}
.t-audit-timeline__day-title {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0 0 12px;
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-base-text-muted, #888);
}
.t-audit-timeline__day-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 6px;
  border-radius: 9px;
  background: var(--tnzi-layout-bg, #f2f3f5);
  color: var(--tnzi-base-text-2, #666);
  font-size: 11px;
  font-weight: 500;
}

/* Whole timeline item is a button that opens the detail drawer. */
.t-audit-timeline__item {
  cursor: pointer;
  border-radius: 6px;
  padding: 4px 8px;
  margin: -4px -8px;
  transition: background-color 0.15s ease;
}
.t-audit-timeline__item:hover {
  background: var(--tnzi-hover-color, rgba(0, 0, 0, 0.03));
}
.t-audit-timeline__item:focus-visible {
  outline: 2px solid var(--tnzi-primary, #6d5ce7);
  outline-offset: 1px;
}
.t-audit-timeline__item-main {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.t-audit-timeline__user {
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-audit-timeline__action {
  color: var(--tnzi-base-text-2, #666);
  font-size: 13px;
}
.t-audit-timeline__elapsed {
  color: var(--tnzi-base-text-muted, #888);
  font-size: 12px;
}
.t-audit-timeline__chevron {
  margin-left: auto;
  color: var(--tnzi-base-text-muted, #bbb);
  opacity: 0;
  transition: opacity 0.15s ease;
  flex-shrink: 0;
}
.t-audit-timeline__item:hover .t-audit-timeline__chevron {
  opacity: 1;
}
.t-audit-timeline__item-sub {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-top: 3px;
  display: flex;
  gap: 12px;
  align-items: baseline;
  flex-wrap: wrap;
}
.t-audit-timeline__route {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace);
  font-size: 12px;
  word-break: break-all;
}
.t-audit-timeline__ip {
  white-space: nowrap;
}
.t-audit-timeline__more {
  text-align: center;
  margin-top: 12px;
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
