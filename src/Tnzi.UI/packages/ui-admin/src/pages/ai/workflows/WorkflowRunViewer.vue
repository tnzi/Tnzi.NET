<template>
  <TContentPage
    :title="t('title')"
    :translate="t"
    card
    scroll="fill"
  >
    <template #actions>
      <NButton size="small" tertiary @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
        {{ t('refresh') }}
      </NButton>
    </template>

    <template #default>
      <TMasterDetailLayout :master-width="320">
        <template #master>
          <NSpace vertical size="small">
            <NSelect
              v-model:value="filters.status"
              :options="statusOptions"
              :placeholder="t('filters.status')"
              size="small"
              clearable
              @update:value="onFilterChange"
            />
            <!-- Phase J: workflow definition dropdown (replaces freeform
                 workflowId input). Hits bridge.workflows.fetch once on
                 mount and reuses the cache. -->
            <NSelect
              v-model:value="filters.workflowDefinitionId"
              :options="workflowOptions"
              :placeholder="t('filters.workflowDefinitionId')"
              size="small"
              filterable
              clearable
              :loading="workflowsLoading"
              @update:value="onFilterChange"
            />
          </NSpace>
          <NSpin :show="listLoading" class="mt-8px">
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
                    {{ statusLabel(run.status) }}
                  </NTag>
                  <span class="t-wf-run-page__run-time">{{ formatTime(run.creationTime) }}</span>
                </div>
                <code class="t-wf-run-page__run-id">{{ shortId(run.id) }}</code>
                <div class="t-wf-run-page__run-meta">
                  <span class="t-wf-run-page__wf-name">{{ workflowName(run.workflowDefinitionId) }}</span>
                  <span>{{ t('list.completed', { n: run.completedStepCount }) }}</span>
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
        </template>
        <template #detail>
          <div v-if="!selectedRunId" class="t-wf-run-page__placeholder">
            {{ t('selectPrompt') }}
          </div>
          <NSpin v-else :show="detailLoading">
            <div v-if="!detail" class="t-wf-run-page__placeholder">{{ EMPTY_DASH }}</div>
            <div v-else>
              <header class="t-wf-run-page__detail-header">
                <div>
                  <h3 class="t-wf-run-page__detail-title">
                    {{ t('detail.title') }}
                    <NTag size="small" :type="statusTypeFor(detail.status)" :bordered="false">
                      {{ statusLabel(detail.status) }}
                    </NTag>
                  </h3>
                  <code class="t-wf-run-page__detail-id">{{ detail.id }}</code>
                </div>
                <NSpace size="small">
                  <NPopconfirm
                    v-if="canResume"
                    @positive-click="onResume"
                  >
                    <template #trigger>
                      <NButton size="small" type="success" ghost :loading="actionPending">
                        <template #icon><TSvgIcon icon="mdi:play" :size="14" /></template>
                        {{ t('detail.resume') }}
                      </NButton>
                    </template>
                    {{ t('detail.confirmResume') }}
                  </NPopconfirm>
                </NSpace>
              </header>

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
                  <!-- Per-step approve / reject when the step is waiting. -->
                  <div v-if="step.awaiting" class="t-wf-run-page__step-actions">
                    <NPopconfirm @positive-click="approveStep(step.id)">
                      <template #trigger>
                        <NButton size="tiny" type="success" ghost>
                          {{ t('detail.approve') }}
                        </NButton>
                      </template>
                      {{ t('detail.confirmApprove') }}
                    </NPopconfirm>
                    <NPopconfirm
                      :disabled="!rejectReasonByStep[step.id]?.trim()"
                      @positive-click="rejectStep(step.id)"
                    >
                      <template #trigger>
                        <NButton
                          size="tiny"
                          type="error"
                          ghost
                          :disabled="!rejectReasonByStep[step.id]?.trim()"
                        >
                          {{ t('detail.reject') }}
                        </NButton>
                      </template>
                      {{ t('detail.confirmReject') }}
                    </NPopconfirm>
                    <NInput
                      v-model:value="rejectReasonByStep[step.id]"
                      size="tiny"
                      :placeholder="t('detail.rejectReasonPlaceholder')"
                      class="max-w-220px"
                    />
                  </div>
                </NTimelineItem>
              </NTimeline>

              <div v-if="!stepTimeline.length" class="t-wf-run-page__empty">
                {{ t('detail.noSteps') }}
              </div>
            </div>
          </NSpin>
        </template>
      </TMasterDetailLayout>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, reactive, ref, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import {
  NSpace, NButton, NSelect, NInput, NSpin, NTimeline, NTimelineItem, NTag, NPopconfirm,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../../components/layout/TMasterDetailLayout.vue'
import { useSafeMessage } from '../../_shared/safe-message'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { makePageTranslator } from '../../_shared/translate'
import type {
  WorkflowDefinitionDto,
  WorkflowExecutionDetailDto,
  WorkflowExecutionSummaryDto,
} from '@tnzi/core/services/ai'

const bridge = createAiBridge({ client: useAdminClient() })
const t = makePageTranslator('ai.workflowRuns')
const route = useRoute()

const message = useSafeMessage()

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
const actionPending = ref(false)

const rejectReasonByStep = reactive<Record<string, string>>({})

const hasMore = computed(() => runs.value.length < total.value)

// Backend serializes WorkflowExecutionStatus as its PascalCase member NAME
// (global JsonStringEnumConverter) and accepts the same on input, so the filter
// sends the member-name string. Mirrors the real enum order + members.
const statusOptions = [
  { label: t('status.running'),          value: 'Running' },
  { label: t('status.completed'),        value: 'Completed' },
  { label: t('status.failed'),           value: 'Failed' },
  { label: t('status.cancelled'),        value: 'Cancelled' },
  { label: t('status.paused'),           value: 'Paused' },
  { label: t('status.awaitingApproval'), value: 'AwaitingApproval' },
  { label: t('status.awaitingInput'),    value: 'AwaitingInput' },
]

// --- Workflow dropdown options (replaces the freeform ID input) ------------
const workflows = ref<WorkflowDefinitionDto[]>([])
const workflowsLoading = ref(false)

const workflowOptions = computed(() =>
  workflows.value.map((wf) => ({ label: wf.name, value: wf.id })),
)

function workflowName(id?: string | null): string {
  if (!id) return EMPTY_DASH
  return workflows.value.find((w) => w.id === id)?.name ?? id.slice(0, 8)
}

async function loadWorkflows(): Promise<void> {
  workflowsLoading.value = true
  try {
    const res = await bridge.workflows.fetch({
      pageIndex: 1, pageSize: 200, searchText: '', filters: {},
    })
    workflows.value = res.items
  } catch {
    // best-effort: dropdown stays empty, free-text still works via URL
  } finally {
    workflowsLoading.value = false
  }
}

function statusTypeFor(status: unknown): 'success' | 'error' | 'warning' | 'info' | 'default' {
  switch (String(status)) {
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Cancelled': return 'warning'
    case 'Running':
    case 'Paused':
    case 'AwaitingApproval':
    case 'AwaitingInput': return 'info'
    default: return 'default'
  }
}

/** i18n label for a WorkflowExecutionStatus member (humanised fallback on miss). */
function statusLabel(status: unknown): string {
  const s = String(status ?? '')
  if (!s) return ''
  return t(`status.${s.charAt(0).toLowerCase()}${s.slice(1)}`)
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
  awaiting?: boolean
}

const stepTimeline = computed<StepRow[]>(() => {
  if (!detail.value) return []
  const out: StepRow[] = []
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
      awaiting: true,
    })
  }
  return out
})

const canResume = computed(() => {
  if (!detail.value) return false
  // `WorkflowExecutionStatus` is a string enum whose values are the member
  // names the backend serializes, so a direct string compare matches the wire
  // payload. A paused / awaiting run, or one with steps pending approval,
  // can be resumed.
  const status = String(detail.value.status)
  return status === 'Paused'
    || status === 'AwaitingApproval'
    || status === 'AwaitingInput'
    || (detail.value.stepsAwaitingApproval?.length ?? 0) > 0
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

// --- Resume + approve/reject ------------------------------------------------
async function onResume(): Promise<void> {
  if (!detail.value) return
  actionPending.value = true
  try {
    await bridge.workflowRuns.resume(detail.value.id)
    message.success(t('detail.resumeSuccess'))
    await loadDetail(detail.value.id)
    await loadRuns(false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : t('detail.resumeError'))
  } finally {
    actionPending.value = false
  }
}

async function approveStep(stepId: string): Promise<void> {
  if (!detail.value) return
  actionPending.value = true
  try {
    await bridge.workflowRuns.approveStep(detail.value.id, stepId)
    message.success(t('detail.approveSuccess'))
    await loadDetail(detail.value.id)
    await loadRuns(false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : t('detail.approveError'))
  } finally {
    actionPending.value = false
  }
}

async function rejectStep(stepId: string): Promise<void> {
  if (!detail.value) return
  const reason = rejectReasonByStep[stepId]?.trim() ?? ''
  if (!reason) {
    message.warning(t('detail.rejectReasonRequired'))
    return
  }
  actionPending.value = true
  try {
    await bridge.workflowRuns.rejectStep(detail.value.id, stepId, reason)
    message.success(t('detail.rejectSuccess'))
    rejectReasonByStep[stepId] = ''
    await loadDetail(detail.value.id)
    await loadRuns(false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : t('detail.rejectError'))
  } finally {
    actionPending.value = false
  }
}

// --- Deep-link: ?runId=… auto-selects the run on mount ----------------------
watch(
  () => route.query.runId,
  (next) => {
    if (typeof next === 'string' && next && next !== selectedRunId.value) {
      void selectRun(next)
    }
  },
)

onMounted(() => {
  void loadWorkflows()
  void loadRuns(false)
  const initial = route.query.runId
  if (typeof initial === 'string' && initial) {
    void selectRun(initial)
  }
})
</script>

<style scoped>
.t-wf-run-page__title {
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
/* Master/detail grid, responsive stacking and pane scroll come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
.t-wf-run-page__run-list {
  list-style: none;
  padding: 0;
  margin: 12px 0 0;
  flex: 1;
  min-height: 0;
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
  background: var(--tnzi-layout-bg);
}
.t-wf-run-page__run-item.is-active {
  background: rgb(var(--tnzi-primary-rgb) / 0.12);
}
.t-wf-run-page__run-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
}
.t-wf-run-page__run-time {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-wf-run-page__run-id {
  font-family: ui-monospace, monospace;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-wf-run-page__run-meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  margin-top: 4px;
}
.t-wf-run-page__wf-name {
  color: var(--tnzi-base-text);
  font-weight: 500;
  font-size: 12.5px;
}
.t-wf-run-page__empty,
.t-wf-run-page__placeholder {
  padding: 32px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
}
.t-wf-run-page__more {
  text-align: center;
  margin-top: 8px;
}
.t-wf-run-page__detail-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--tnzi-border);
  margin-bottom: 12px;
}
.t-wf-run-page__detail-title {
  margin: 0 0 4px;
  font-size: 16px;
  font-weight: 600;
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.t-wf-run-page__detail-id {
  font-family: ui-monospace, monospace;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-wf-run-page__detail-meta {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 12px;
  font-size: 13px;
}
.t-wf-run-page__detail-meta span {
  color: var(--tnzi-base-text-muted);
  margin-right: 6px;
}
.t-wf-run-page__io {
  margin-bottom: 12px;
}
.t-wf-run-page__io pre {
  margin: 8px 0 0;
  padding: 8px;
  background: var(--tnzi-layout-bg);
  border-radius: 4px;
  font-size: 12px;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 200px;
  overflow: auto;
}
.t-wf-run-page__section-title {
  margin: 16px 0 8px;
  font-size: 14px;
  font-weight: 600;
}
.t-wf-run-page__step-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  margin-top: 8px;
}
</style>
