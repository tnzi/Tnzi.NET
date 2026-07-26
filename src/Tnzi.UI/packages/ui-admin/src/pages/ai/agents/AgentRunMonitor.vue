<template>
  <!--
    AgentRunMonitor - run trace viewer.
    Routed at /admin/ai/agents/:agentId/runs/:runId? - pure monitor (no CRUD).

    Trace delivery: the backend exposes GET /admin/agent-runs/{runId}/traces
    (DefaultAgentRunAdminController) returning the recorded AgentRunTrace list -
    there is no SSE tail endpoint. So the page fetches traces through the bridge
    and, while the selected run is non-terminal, polls every 3s; polling stops
    on a terminal transition and on unmount. Read-only view - no CRUD, inline
    status banners instead of a message provider.
  -->
  <TContentPage
    :title="t('title')"
    :translate="t"
    :back="backPath"
    scroll="fill"
  >
    <template #actions>
      <NButton size="small" tertiary @click="handleRefresh">
        {{ t('refresh') }}
      </NButton>
    </template>

    <template #default>
      <TMasterDetailLayout :master-width="320" :bordered="false" :detail-scroll="false">
        <template #master>
          <aside class="t-run-monitor__list" data-test="run-list">
            <div v-if="listError" class="t-run-monitor__error" role="alert">
              {{ listError }}
            </div>
            <div v-else-if="listLoading" class="t-run-monitor__loading">
              {{ t('loading') }}
            </div>
            <ul v-else-if="runs.length" class="t-run-monitor__list-items">
              <li
                v-for="run in runs"
                :key="run.id"
                class="t-run-monitor__list-row"
                :class="[
                  runStatusClass[String(run.status)] ?? '',
                  { 'is-selected': run.id === selectedRunId },
                ]"
                :data-run-id="run.id"
                @click="selectRun(run.id)"
              >
                <div class="t-run-monitor__row-id">{{ shortId(run.id) }}</div>
                <div class="t-run-monitor__row-status">{{ statusLabel(run.status) }}</div>
                <div class="t-run-monitor__row-time">{{ formatTime(run.creationTime) }}</div>
                <div class="t-run-monitor__row-dur">{{ run.durationMs }}ms</div>
              </li>
            </ul>
            <div v-else class="t-run-monitor__placeholder">
              {{ t('empty') }}
            </div>
          </aside>
        </template>

        <template #detail>
          <NCard
            size="small"
            :bordered="false"
            class="t-run-monitor__detail"
            data-test="run-detail"
          >
            <div v-if="!selectedRun" class="t-run-monitor__placeholder">
              {{ t('selectPrompt') }}
            </div>
            <template v-else>
              <div class="t-run-monitor__meta">
                <div><strong>{{ t('field.id') }}:</strong> {{ selectedRun.id }}</div>
                <div>
                  <strong>{{ t('field.status') }}:</strong>
                  <NTag size="small" :type="statusTagType(selectedRun.status)" :bordered="false">
                    {{ statusLabel(selectedRun.status) }}
                  </NTag>
                </div>
                <div>
                  <strong>{{ t('field.started') }}:</strong>
                  {{ formatTime(selectedRun.creationTime) }}
                </div>
                <div>
                  <strong>{{ t('field.duration') }}:</strong>
                  {{ selectedRun.durationMs }}ms
                </div>
                <NButton
                  v-if="isRunning(selectedRun)"
                  type="error"
                  ghost
                  size="small"
                  :loading="cancelling"
                  class="t-run-monitor__cancel"
                  @click="handleCancel"
                >
                  {{ t('cancel') }}
                </NButton>
              </div>

              <div v-if="cancelStatus" class="t-run-monitor__status" :data-state="cancelStatus.kind">
                {{ cancelStatus.message }}
              </div>

              <div v-if="streamError" class="t-run-monitor__banner" role="alert" data-test="stream-error">
                {{ streamError }}
              </div>

              <h3 class="t-run-monitor__trace-title">{{ t('trace') }}</h3>
              <ol class="t-run-monitor__trace" data-test="trace-list">
                <li
                  v-for="(evt, idx) in traceEvents"
                  :key="idx"
                  class="t-run-monitor__trace-row"
                >
                  <span class="t-run-monitor__trace-type">{{ evt.type ?? 'event' }}</span>
                  <span class="t-run-monitor__trace-content">{{ evt.content ?? '' }}</span>
                </li>
              </ol>
              <div v-if="!traceEvents.length" class="t-run-monitor__placeholder">
                {{ t('traceWaiting') }}
              </div>
            </template>
          </NCard>
        </template>
      </TMasterDetailLayout>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { makePageTranslator } from '../../_shared/translate'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { NCard, NButton, NTag } from 'naive-ui'
import type { StatusType } from '@tnzi/ui'
import TContentPage from '../../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../../components/layout/TMasterDetailLayout.vue'
import { runStatusClass, type RunTraceEvent } from './agent-run-monitor-config'
import type { AgentRunDto, AgentRunTraceDto } from '@tnzi/core/services/ai'

const route = useRoute()
const router = useRouter()
const bridge = createAiBridge({ client: useAdminClient() })

// Locale keys live under `ai.runMonitor.monitor.*` (nested), so pin the page
// namespace there - the page uses short keys like t('title')/t('back').
const t = makePageTranslator('ai.runMonitor.monitor')

// suppress unused - referenced in template via runStatusClass binding
void runStatusClass

const runs = ref<AgentRunDto[]>([])
const listLoading = ref(false)
const listError = ref<string | null>(null)

const selectedRunId = ref<string | null>(null)
const selectedRun = computed<AgentRunDto | null>(
  () => runs.value.find((r) => r.id === selectedRunId.value) ?? null,
)

const traceEvents = ref<RunTraceEvent[]>([])
const streamError = ref<string | null>(null)
const cancelling = ref(false)
const cancelStatus = ref<{ kind: 'ok' | 'err'; message: string } | null>(null)

// Poll handle for the trace refresh loop (non-terminal runs only).
let pollTimer: ReturnType<typeof setInterval> | null = null
const POLL_INTERVAL_MS = 3000

// Terminal run states - no further traces will arrive, so polling stops.
const TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled'])

const agentId = computed(() => {
  const raw = route.params?.agentId
  if (Array.isArray(raw)) return raw[0] ?? null
  return typeof raw === 'string' && raw.length > 0 ? raw : null
})

// TPageHeader.back 只收 path 字符串;经 router.resolve 从命名路由取 path,保持部署前缀无关
const backPath = computed(() => {
  if (!agentId.value) return true
  try {
    return router?.resolve({ name: 'ai.agents.detail', params: { id: agentId.value } }).path ?? true
  } catch {
    return true
  }
})

function shortId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id
}

function formatTime(s: string | null | undefined): string {
  if (!s) return EMPTY_DASH
  try {
    return new Date(s).toLocaleString()
  } catch {
    return s
  }
}

/** Non-terminal (cancellable / still-polling) run. */
function isRunning(run: AgentRunDto): boolean {
  return !TERMINAL_STATUSES.has(String(run.status))
}

/** i18n label for an AgentRunStatus member name (humanised fallback on miss). */
function statusLabel(status: unknown): string {
  const s = String(status ?? '')
  if (!s) return ''
  return t(`status.${s.charAt(0).toLowerCase()}${s.slice(1)}`)
}

/** Semantic colour for the status badge. */
function statusTagType(status: unknown): StatusType {
  switch (String(status)) {
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Cancelled': return 'warning'
    case 'Running':
    case 'Pending':
    case 'AwaitingApproval':
    case 'RequiresClarification': return 'info'
    default: return 'default'
  }
}

async function loadRuns() {
  listLoading.value = true
  listError.value = null
  try {
    const result = await bridge.agentRuns.fetch({
      pageIndex: 1,
      pageSize: 30,
      sortField: 'creationTime',
      sortOrder: 'desc',
      searchText: '',
      filters: agentId.value ? { agentId: agentId.value } : {},
    })
    runs.value = result.items
    // Auto-select a run if route has runId or first row otherwise
    const routeRunId = currentRouteRunId()
    if (routeRunId) {
      selectRun(routeRunId)
    }
  } catch (e) {
    listError.value = (e as Error).message ?? 'Failed to load runs'
  } finally {
    listLoading.value = false
  }
}

function currentRouteRunId(): string | null {
  const raw = route.params?.runId
  if (Array.isArray(raw)) return raw[0] ?? null
  return typeof raw === 'string' && raw.length > 0 ? raw : null
}

/** Map a backend trace DTO to the loose display shape the trace panel renders. */
function mapTrace(trace: AgentRunTraceDto): RunTraceEvent {
  return {
    type: trace.eventType,
    timestamp: trace.creationTime,
    content: trace.eventData ?? '',
    raw: trace,
  }
}

function stopPolling(): void {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

/** Fetch the recorded traces for a run and refresh the panel. */
async function loadTraces(runId: string): Promise<void> {
  try {
    const traces = await bridge.agentRuns.getTraces(runId)
    // Ignore late responses for a run the user already navigated away from.
    if (runId !== selectedRunId.value) return
    traceEvents.value = traces.map(mapTrace)
    streamError.value = null
  } catch (e) {
    if (runId !== selectedRunId.value) return
    streamError.value = (e as Error).message ?? t('streamError')
  }
}

/** Quietly refresh run statuses so a terminal transition ends the poll loop. */
async function refreshRunStatuses(): Promise<void> {
  try {
    const result = await bridge.agentRuns.fetch({
      pageIndex: 1,
      pageSize: 30,
      sortField: 'creationTime',
      sortOrder: 'desc',
      searchText: '',
      filters: agentId.value ? { agentId: agentId.value } : {},
    })
    runs.value = result.items
  } catch {
    // Keep the last-known list; the next tick retries.
  }
}

/** Start the 3s trace poll for a non-terminal run; no-op for terminal runs. */
function scheduleTracePolling(runId: string): void {
  stopPolling()
  const run = runs.value.find((r) => r.id === runId)
  if (!run || !isRunning(run)) return
  pollTimer = setInterval(async () => {
    if (runId !== selectedRunId.value) {
      stopPolling()
      return
    }
    await loadTraces(runId)
    await refreshRunStatuses()
    const current = runs.value.find((r) => r.id === runId)
    if (!current || !isRunning(current)) stopPolling()
  }, POLL_INTERVAL_MS)
}

function selectRun(runId: string) {
  selectedRunId.value = runId
  traceEvents.value = []
  streamError.value = null
  void loadTraces(runId).then(() => scheduleTracePolling(runId))
}

async function handleRefresh() {
  await loadRuns()
}

async function handleCancel() {
  if (!selectedRunId.value) return
  cancelling.value = true
  cancelStatus.value = null
  try {
    const cancelledId = selectedRunId.value
    await bridge.agentRuns.cancel(cancelledId)
    cancelStatus.value = { kind: 'ok', message: t('cancelled') }
    // Refresh list to pick up the new (now terminal) status, then re-sync the
    // trace panel + stop the poll loop for the cancelled run.
    await refreshRunStatuses()
    await loadTraces(cancelledId)
    scheduleTracePolling(cancelledId)
  } catch (e) {
    cancelStatus.value = { kind: 'err', message: (e as Error).message ?? 'Cancel failed' }
  } finally {
    cancelling.value = false
  }
}

onMounted(() => {
  void loadRuns()
})

onBeforeUnmount(() => {
  stopPolling()
})

// Refetch when agent id changes
watch(
  () => route.params?.agentId,
  (next, prev) => {
    if (next === prev) return
    selectedRunId.value = null
    stopPolling()
    void loadRuns()
  },
)

defineExpose({ selectRun, runs, traceEvents, selectedRunId })
</script>

<style scoped>
/* Master/detail split, responsive stacking and pane fill-height come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
/* The run list is its own white panel - symmetric with the detail NCard on
   the right - so the master column doesn't sit bare on the page canvas.
   `overflow: hidden` clips the rows to the rounded corners; the inner list
   scrolls. Mirrors the detail card's radius + soft shadow. */
.t-run-monitor__list {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg, #fff));
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
.t-run-monitor__list-items {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  list-style: none;
  margin: 0;
  padding: 0;
}
.t-run-monitor__list-row {
  display: grid;
  grid-template-columns: 1fr auto;
  padding: 8px 12px;
  cursor: pointer;
  border-bottom: 1px solid var(--tnzi-border);
}
.t-run-monitor__list-row.is-selected {
  background: rgb(var(--tnzi-primary-rgb) / 0.12);
}
.t-run-monitor__detail {
  /* NCard supplies the chrome (padding, border, surface). We only add
     the soft shadow + radius for parity with TCrudPage list-card. */
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
.t-run-monitor__meta > div {
  margin-bottom: 4px;
}
.t-run-monitor__cancel {
  margin-top: 8px;
}
.t-run-monitor__trace {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 360px;
  overflow-y: auto;
}
.t-run-monitor__trace-row {
  padding: 4px 0;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-run-monitor__trace-type {
  display: inline-block;
  margin-right: 8px;
  font-weight: 600;
  color: var(--tnzi-base-text-muted);
}
.t-run-monitor__placeholder {
  padding: 32px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
}
.t-run-monitor__error,
.t-run-monitor__banner {
  color: var(--tnzi-error);
  padding: 8px 0;
}
.t-run-monitor__status[data-state='ok'] {
  color: var(--tnzi-success);
}
.t-run-monitor__status[data-state='err'] {
  color: var(--tnzi-error);
}
</style>
