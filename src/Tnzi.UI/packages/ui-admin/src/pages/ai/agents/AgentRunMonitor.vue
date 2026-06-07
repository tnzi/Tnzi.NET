<template>
  <!--
    AgentRunMonitor — Phase 5 Task 5.4 SSE streaming page.
    Routed at /admin/ai/agents/:agentId/runs/:runId? — pure monitor (no CRUD).

    Lessons applied:
      - 1: setActivePinia in tests
      - 2: real DTOs from @tnzi/core/services/ai (AgentRunDto)
      - 7: inline status banner instead of useMessage
      - 9: read-only view — no CRUD operations
      - 12: do NOT add a stub method to the bridge (tail() rejects by design);
            this page goes around the bridge for SSE — the only Phase 5 page
            that does so, with a documented reason

    NEW lesson 16: Browser-native EventSource in Vue pages — capture the
    constructed instance in a local ref, close on selection change AND
    on unmount, and accept the URL as a constructible string so tests can
    stub global EventSource without DI gymnastics.

    Backend gap (documented): Tnzi.AI exposes only POST /admin/agents/{id}/run/stream
    which starts a NEW run. No GET /admin/agent-runs/{runId}/stream tail
    endpoint exists yet. This page assumes that conventional URL — when the
    backend lands the endpoint the URL builder below stays correct. Until
    then, the page will surface a connection error in production and tests
    cover the contract via a mocked EventSource.
  -->
  <TContentPage
    :title="t('title')"
    :translate="t"
    :back="`/admin/ai/agents/${agentId}`"
    scroll="fill"
  >
    <template #actions>
      <NButton size="small" tertiary @click="handleRefresh">
        {{ t('refresh') }}
      </NButton>
    </template>

    <template #default>
      <div class="t-run-monitor__body">
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
                runStatusClass[run.status as unknown as string] ?? '',
                { 'is-selected': run.id === selectedRunId },
              ]"
              :data-run-id="run.id"
              @click="selectRun(run.id)"
            >
              <div class="t-run-monitor__row-id">{{ shortId(run.id) }}</div>
              <div class="t-run-monitor__row-status">{{ run.status }}</div>
              <div class="t-run-monitor__row-time">{{ formatTime(run.creationTime) }}</div>
              <div class="t-run-monitor__row-dur">{{ run.durationMs }}ms</div>
            </li>
          </ul>
          <div v-else class="t-run-monitor__placeholder">
            {{ t('empty') }}
          </div>
        </aside>

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
              <div><strong>{{ t('field.status') }}:</strong> {{ selectedRun.status }}</div>
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
      </div>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute } from 'vue-router'
import { makePageTranslator } from '../../_shared/translate'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { NCard, NButton } from 'naive-ui'
import TContentPage from '../../../components/layout/TContentPage.vue'
import { runStatusClass, type RunTraceEvent } from './agent-run-monitor-config'
import type { AgentRunDto } from '@tnzi/core/services/ai'

const route = useRoute()
const bridge = createAiBridge({ client: useAdminClient() })

// Locale keys live under `ai.runMonitor.monitor.*` (nested), so pin the page
// namespace there — the page uses short keys like t('title')/t('back').
const t = makePageTranslator('ai.runMonitor.monitor')

// suppress unused — referenced in template via runStatusClass binding
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

let eventSource: EventSource | null = null

const agentId = computed(() => {
  const raw = route.params?.agentId
  if (Array.isArray(raw)) return raw[0] ?? null
  return typeof raw === 'string' && raw.length > 0 ? raw : null
})

function shortId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id
}

function formatTime(s: string | null | undefined): string {
  if (!s) return '—'
  try {
    return new Date(s).toLocaleString()
  } catch {
    return s
  }
}

function isRunning(run: AgentRunDto): boolean {
  const status = String(run.status)
  return status === 'Running' || status === '0' || status === 'Pending'
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

/**
 * Build the SSE URL for tailing a run. The backend conventional path is
 * /admin/agent-runs/{runId}/stream — see the page-level comment for the
 * documented backend gap. Constructed inline to avoid coupling this page to
 * a hypothetical bridge method that would otherwise reject.
 */
function buildStreamUrl(runId: string): string {
  // Use a relative URL — EventSource resolves against window.location.origin.
  return `/api/admin/agent-runs/${encodeURIComponent(runId)}/stream`
}

function closeStream() {
  if (eventSource) {
    try {
      eventSource.close()
    } catch {
      /* ignore */
    }
    eventSource = null
  }
}

function openStream(runId: string) {
  closeStream()
  traceEvents.value = []
  streamError.value = null

  if (typeof EventSource === 'undefined') {
    streamError.value = 'EventSource unavailable in this environment'
    return
  }

  try {
    const url = buildStreamUrl(runId)
    const es = new EventSource(url)
    eventSource = es

    es.onmessage = (msg: MessageEvent) => {
      let parsed: RunTraceEvent
      try {
        parsed = JSON.parse(msg.data) as RunTraceEvent
      } catch {
        parsed = { type: 'raw', content: String(msg.data) }
      }
      traceEvents.value = [...traceEvents.value, parsed]
    }

    es.onerror = () => {
      streamError.value = t('streamError')
    }
  } catch (e) {
    streamError.value = (e as Error).message ?? 'Failed to open stream'
  }
}

function selectRun(runId: string) {
  selectedRunId.value = runId
  openStream(runId)
}

async function handleRefresh() {
  await loadRuns()
}

async function handleCancel() {
  if (!selectedRunId.value) return
  cancelling.value = true
  cancelStatus.value = null
  try {
    await bridge.agentRuns.cancel(selectedRunId.value)
    cancelStatus.value = { kind: 'ok', message: t('cancelled') }
    // Refresh list to pick up new status
    await loadRuns()
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
  closeStream()
})

// Refetch when agent id changes
watch(
  () => route.params?.agentId,
  (next, prev) => {
    if (next === prev) return
    selectedRunId.value = null
    closeStream()
    void loadRuns()
  },
)

defineExpose({ selectRun, runs, traceEvents, selectedRunId })
</script>

<style scoped>
.t-run-monitor__body {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
  height: 100%;
  min-height: 480px;
}
/* Phone: stack the run list above the run detail. */
@media (max-width: 767px) {
  .t-run-monitor__body {
    grid-template-columns: 1fr;
    height: auto;
    min-height: 0;
  }
}
/* The run list is its own white panel — symmetric with the detail NCard on
   the right — so the master column doesn't sit bare on the page canvas.
   `overflow: hidden` clips the rows to the rounded corners; the inner list
   scrolls. Mirrors the detail card's radius + soft shadow. */
.t-run-monitor__list {
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--tnzi-container-bg, #fff);
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
