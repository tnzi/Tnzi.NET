<template>
  <div class="t-wf-run-page t-page-scroll">
    <NCard :title="t('title')" :bordered="false">
      <template #header-extra>
        <NSpace>
          <NButton size="small" @click="refresh">{{ t('refresh') }}</NButton>
        </NSpace>
      </template>

      <div class="t-wf-run-page__layout">
        <!-- Left: runs list -->
        <aside class="t-wf-run-page__list">
          <NSpace vertical size="small">
            <NSelect
              v-model:value="filters.status"
              :options="statusOptions"
              :placeholder="t('filters.status')"
              size="small"
              clearable
              @update:value="onFilterChange"
            />
            <NInput
              v-model:value="filters.workflowDefinitionId"
              :placeholder="t('filters.workflowId')"
              size="small"
              clearable
              @change="onFilterChange"
            />
          </NSpace>
          <NSpin :show="listLoading" style="margin-top: 8px">
            <div v-if="!runs.length && !listLoading" class="t-wf-run-page__empty">
              {{ t('empty') }}
            </div>
            <ul v-else class="t-wf-run-page__run-list">
              <li
                v-for="run in runs"
                :key="run.id"
                class="t-wf-run-page__run-item"
                :class="{ 'is-active': run.id === selectedRunId }"
                @click="selectRun(run.id)"
              >
                <div class="t-wf-run-page__run-header">
                  <NTag size="small" :type="statusTypeFor(run.status)" :bordered="false">
                    {{ run.status }}
                  </NTag>
                  <span class="t-wf-run-page__run-time">{{ formatTime(run.creationTime) }}</span>
                </div>
                <code class="t-wf-run-page__run-id">{{ shortId(run.id) }}</code>
                <div class="t-wf-run-page__run-meta">
                  {{ t('list.completed', { n: run.completedStepCount }) }}
                  <span v-if="run.awaitingApprovalCount > 0">
                    · {{ t('list.awaiting', { n: run.awaitingApprovalCount }) }}
                  </span>
                </div>
              </li>
            </ul>
          </NSpin>
          <div v-if="hasMore" class="t-wf-run-page__more">
            <NButton size="tiny" @click="loadMore">{{ t('loadMore') }}</NButton>
          </div>
        </aside>

        <!-- Right: selected run detail / step timeline -->
        <section class="t-wf-run-page__detail">
          <div v-if="!selectedRunId" class="t-wf-run-page__placeholder">
            {{ t('selectPrompt') }}
          </div>
          <NSpin v-else :show="detailLoading">
            <div v-if="!detail" class="t-wf-run-page__placeholder">—</div>
            <div v-else>
              <header class="t-wf-run-page__detail-header">
                <div>
                  <h3 class="t-wf-run-page__detail-title">
                    {{ t('detail.title') }}
                    <NTag size="small" :type="statusTypeFor(detail.status)" :bordered="false">
                      {{ detail.status }}
                    </NTag>
                  </h3>
                  <code class="t-wf-run-page__detail-id">{{ detail.id }}</code>
                </div>
                <div class="t-wf-run-page__detail-meta">
                  <div>
                    <span>{{ t('detail.startedAt') }}:</span>
                    {{ formatTime(detail.creationTime, true) }}
                  </div>
                  <div v-if="detail.completedTime">
                    <span>{{ t('detail.completedAt') }}:</span>
                    {{ formatTime(detail.completedTime, true) }}
                  </div>
                  <div v-if="duration">
                    <span>{{ t('detail.duration') }}:</span>
                    {{ duration }}
                  </div>
                </div>
              </header>

              <details v-if="detail.initialInput" class="t-wf-run-page__io">
                <summary>{{ t('detail.initialInput') }}</summary>
                <pre>{{ detail.initialInput }}</pre>
              </details>

              <h4 class="t-wf-run-page__section-title">{{ t('detail.steps') }}</h4>
              <NTimeline size="medium">
                <NTimelineItem
                  v-for="step in stepTimeline"
                  :key="step.id"
                  :type="step.type"
                  :title="step.id"
                  :time="step.label"
                >
                  <details v-if="step.output">
                    <summary>{{ t('detail.output') }}</summary>
                    <pre>{{ step.output }}</pre>
                  </details>
                </NTimelineItem>
              </NTimeline>

              <div v-if="!stepTimeline.length" class="t-wf-run-page__empty">
                {{ t('detail.noSteps') }}
              </div>
            </div>
          </NSpin>
        </section>
      </div>
    </NCard>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref, onMounted } from 'vue'
import {
  NCard, NSpace, NButton, NSelect, NInput, NSpin, NTimeline, NTimelineItem, NTag,
  useMessage,
} from 'naive-ui'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { makePageTranslator } from '../../_shared/translate'
import type { WorkflowExecutionDetailDto, WorkflowExecutionSummaryDto } from '@tnzi/core/services/ai'

const bridge = createAiBridge({ client: useAdminClient() })
const t = makePageTranslator('ai.workflowRuns')

let message: { error(s: string): void }
try {
  message = useMessage()
} catch {
  message = { error: () => {} }
}

interface Filters { status?: string; workflowDefinitionId?: string }
const filters = reactive<Filters>({})
const pageSize = 20
const pageIndex = ref(1)
const runs = ref<WorkflowExecutionSummaryDto[]>([])
const total = ref(0)
const selectedRunId = ref<string | null>(null)
const detail = ref<WorkflowExecutionDetailDto | null>(null)
const listLoading = ref(false)
const detailLoading = ref(false)

const hasMore = computed(() => runs.value.length < total.value)

const statusOptions = [
  { label: 'Pending', value: 'Pending' },
  { label: 'Running', value: 'Running' },
  { label: 'Completed', value: 'Completed' },
  { label: 'Failed', value: 'Failed' },
  { label: 'Paused', value: 'Paused' },
  { label: 'Cancelled', value: 'Cancelled' },
]

function statusTypeFor(status: unknown): 'success' | 'error' | 'warning' | 'info' | 'default' {
  switch (status) {
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Cancelled': return 'warning'
    case 'Running':
    case 'Pending':
    case 'Paused': return 'info'
    default: return 'default'
  }
}

function shortId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id
}

function formatTime(v?: string | Date | null, withDate = false): string {
  if (!v) return ''
  try {
    const d = new Date(v)
    const hh = String(d.getHours()).padStart(2, '0')
    const mm = String(d.getMinutes()).padStart(2, '0')
    if (!withDate) return `${hh}:${mm}`
    const y = d.getFullYear()
    const M = String(d.getMonth() + 1).padStart(2, '0')
    const D = String(d.getDate()).padStart(2, '0')
    return `${y}-${M}-${D} ${hh}:${mm}`
  } catch {
    return ''
  }
}

const duration = computed(() => {
  if (!detail.value?.creationTime || !detail.value?.completedTime) return ''
  try {
    const start = new Date(detail.value.creationTime).getTime()
    const end = new Date(detail.value.completedTime).getTime()
    const ms = end - start
    if (ms < 1000) return `${ms}ms`
    if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`
    return `${Math.round(ms / 1000)}s`
  } catch {
    return ''
  }
})

interface StepRow {
  id: string
  type: 'success' | 'warning' | 'info' | 'default' | 'error'
  label: string
  output?: string
}

const stepTimeline = computed<StepRow[]>(() => {
  if (!detail.value) return []
  const out: StepRow[] = []
  // Completed steps first (ordered as backend returned them).
  for (const id of detail.value.completedStepIds ?? []) {
    out.push({
      id,
      type: 'success',
      label: t('detail.stepCompleted'),
      output: detail.value.stepOutputs?.[id],
    })
  }
  for (const id of detail.value.stepsAwaitingApproval ?? []) {
    out.push({
      id,
      type: 'warning',
      label: t('detail.stepAwaitingApproval'),
    })
  }
  return out
})

async function loadRuns(append = false): Promise<void> {
  listLoading.value = true
  try {
    const result = await bridge.workflowRuns.fetch({
      pageIndex: pageIndex.value,
      pageSize,
      sortField: 'creationTime',
      sortOrder: 'desc',
      searchText: '',
      filters: { ...filters },
    })
    runs.value = append ? [...runs.value, ...result.items] : result.items
    total.value = result.totalCount
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    if (!append) runs.value = []
  } finally {
    listLoading.value = false
  }
}

// Monotonic token — see EntityRole.vue for the pattern; clicking through
// the runs list fast otherwise lets an older detail land on top of a newer one.
let detailFetchToken = 0

async function loadDetail(id: string): Promise<void> {
  const myToken = ++detailFetchToken
  detailLoading.value = true
  try {
    const result = await bridge.workflowRuns.getDetail(id)
    if (myToken !== detailFetchToken) return
    detail.value = result
  } catch (e) {
    if (myToken !== detailFetchToken) return
    message.error(e instanceof Error ? e.message : String(e))
    detail.value = null
  } finally {
    if (myToken === detailFetchToken) detailLoading.value = false
  }
}

async function selectRun(id: string): Promise<void> {
  selectedRunId.value = id
  await loadDetail(id)
}

async function refresh(): Promise<void> {
  pageIndex.value = 1
  await loadRuns(false)
  if (selectedRunId.value) await loadDetail(selectedRunId.value)
}

async function loadMore(): Promise<void> {
  pageIndex.value += 1
  await loadRuns(true)
}

function onFilterChange(): void {
  void refresh()
}

onMounted(() => {
  void loadRuns(false)
})
</script>

<style scoped>
.t-wf-run-page {
  padding: 16px;
}
.t-wf-run-page__layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
  min-height: 520px;
}
.t-wf-run-page__list {
  border-right: 1px solid var(--tnzi-base-border, #efeff5);
  padding-right: 16px;
}
.t-wf-run-page__run-list {
  list-style: none;
  padding: 0;
  margin: 12px 0 0;
  max-height: 60vh;
  overflow: auto;
}
.t-wf-run-page__run-item {
  padding: 10px 12px;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  margin-bottom: 4px;
  transition: background-color 0.15s;
}
.t-wf-run-page__run-item:hover {
  background: var(--tnzi-base-fill, #f5f5f7);
}
.t-wf-run-page__run-item.is-active {
  background: var(--tnzi-primary-color-suppl, rgba(6, 182, 212, 0.12));
}
.t-wf-run-page__run-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
}
.t-wf-run-page__run-time {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-wf-run-page__run-id {
  font-family: var(--tnzi-font-family-mono, ui-monospace, monospace);
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-wf-run-page__run-meta {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-top: 2px;
}
.t-wf-run-page__detail {
  padding: 0 4px;
}
.t-wf-run-page__placeholder,
.t-wf-run-page__empty {
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 60px 16px;
  font-size: 13px;
}
.t-wf-run-page__detail-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
}
.t-wf-run-page__detail-title {
  margin: 0 0 4px;
  font-size: 18px;
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-wf-run-page__detail-id {
  font-family: var(--tnzi-font-family-mono, ui-monospace, monospace);
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-wf-run-page__detail-meta {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  text-align: right;
}
.t-wf-run-page__detail-meta span {
  font-weight: 500;
  margin-right: 4px;
}
.t-wf-run-page__io {
  margin: 12px 0;
}
.t-wf-run-page__io summary {
  cursor: pointer;
  font-size: 13px;
  color: var(--tnzi-primary-color, #06B6D4);
}
.t-wf-run-page__io pre {
  margin-top: 8px;
  padding: 8px;
  background: var(--tnzi-base-fill, #f5f5f7);
  border-radius: 4px;
  font-size: 12px;
  max-height: 200px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-word;
}
.t-wf-run-page__section-title {
  margin: 20px 0 12px;
  font-size: 14px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-wf-run-page__more {
  text-align: center;
  margin-top: 8px;
}
</style>
