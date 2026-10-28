<template>
  <TCrudPage
    :search-fields="runSearchFields"
    :show-default-search="false"
    :state="crud"
    :all-columns="runColumns"
    :title="title"
    :translate="t"
    :show-create="false"
    :show-batch="false"
    :row-actions="rowActions"
    :detail-width="820"
    :row-props="rowProps"
  >
    <template #detail="{ data }">
      <div v-if="data" class="cli-run-detail">
        <TDescriptions :items="detailItems(data as CliRunDto)" />

        <div v-if="(data as CliRunDto).error" class="cli-run-detail__error">
          <TSvgIcon icon="mdi:alert-circle-outline" :size="14" />
          <span>{{ (data as CliRunDto).error }}</span>
        </div>

        <section class="cli-run-detail__section">
          <header class="cli-run-detail__section-head">
            <span>{{ t('detail.timeline') }}</span>
            <NButton size="tiny" quaternary :loading="loadingEvents" @click="reloadEvents">
              <template #icon><TSvgIcon icon="mdi:refresh" :size="13" /></template>
              {{ t('actions.refresh') }}
            </NButton>
          </header>

          <div v-if="eventsError" class="cli-run-detail__error">
            <TSvgIcon icon="mdi:alert-circle-outline" :size="14" />
            <span>{{ eventsError }}</span>
          </div>

          <TEmpty v-if="!events.length && !loadingEvents && !eventsError" :text="t('detail.noEvents')" />

          <NTimeline v-else>
            <NTimelineItem
              v-for="event in events"
              :key="event.id"
              :type="eventTone(event.type)"
              :title="eventTitle(event)"
              :time="formatDateTime(event.creationTime)"
            >
              <p v-if="event.content" class="cli-run-detail__event-body">{{ event.content }}</p>
              <pre v-if="event.output" class="cli-run-detail__event-output">{{ event.output }}</pre>
            </NTimelineItem>
          </NTimeline>
        </section>
      </div>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { onActivated, onDeactivated, onUnmounted, ref, watch } from 'vue'
import { NButton, NTimeline, NTimelineItem } from 'naive-ui'
import { formatDateTime } from '@tnzi/core'
import { EMPTY_DASH, TDescriptions, TEmpty, TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { type RowAction } from '../../../headless/row-actions'
import { useSafeMessage } from '../../_shared/safe-message'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { makePageTranslator } from '../../../i18n/translate'
import { useAdminClient } from '../../../plugin/client'
import { toLocalDayBounds } from '../../../utils/calendar-date'
import {
  createCliAgentBridge,
  CliAgentEventType,
  CLI_RUN_TERMINAL_STATUSES,
  type CliRunDto,
  type CliRunMessageDto,
  type CliRunQueryDto,
} from '../../../services/bridges/cli-agent-bridge'
import { formatRunCost, formatRunDuration, runColumns, runSearchFields, runStatusBadgeMapping } from './cli-runtime-config'

/** How often a still-running run and its timeline are re-read while its drawer is open. */
const LIVE_POLL_MS = 3000

const bridge = createCliAgentBridge({ client: useAdminClient() })
const message = useSafeMessage()
const t = makePageTranslator('ai.cliRuns')
const { can } = usePermissionGuard()
const title = 'title'

const events = ref<CliRunMessageDto[]>([])
const loadingEvents = ref(false)
/** A refused timeline read. Shown instead of "no events recorded", which it is not. */
const eventsError = ref<string | null>(null)
let pollTimer: ReturnType<typeof setInterval> | null = null

const crud = useCrudPage<CliRunDto>({
  pageId: 'ai.cli-runs',
  columns: runColumns,
  rowKey: (r) => String(r.id ?? ''),
  permission: 'ai.cliRun',
  // `useCrudPage` hands over a nested `{ filters: {...} }`, while the API binds
  // every filter at the top level. Flattening here is what makes the declared
  // search fields actually reach the backend - the previous blanket cast
  // type-checked fine and shipped `filters[status]`, which the API ignores, so
  // the control would have looked like it did nothing.
  // The date filters arrive as picked calendar dates and runs are filtered by
  // their creation timestamp, so they go out as the bounds of those local days.
  fetchData: (query) =>
    bridge.runs.list({
      pageIndex: query.pageIndex,
      pageSize: query.pageSize,
      ...toLocalDayBounds(query.filters ?? {}, 'startTime', 'endTime'),
    } as CliRunQueryDto),
})

/**
 * The timeline is replayed from persisted events and re-fetched on a timer
 * while the run is still going.
 *
 * Deliberately polling rather than holding the SSE stream open: the admin
 * monitor only needs "what happened", and a three-second refresh delivers that
 * without an auth-header'd EventSource whose reconnect semantics would have to
 * be re-derived here. Consumers that want a true live feed can use
 * `streamCliRun` from `@tnzi/core`, which resumes precisely from a sequence.
 */
async function reloadEvents() {
  const run = crud.formModal.formData.value as CliRunDto | null
  if (!run?.id) {
    events.value = []
    return
  }

  loadingEvents.value = true
  try {
    const list = await bridge.runs.messages(run.id)
    // The drawer may have moved on to another run while this one was loading.
    if ((crud.formModal.formData.value as CliRunDto | null)?.id !== run.id) return
    events.value = list
    eventsError.value = null
  } catch (e) {
    if ((crud.formModal.formData.value as CliRunDto | null)?.id !== run.id) return
    // Keep what is on screen: a poll that fails once must not blank the timeline.
    eventsError.value = e instanceof Error ? e.message : String(e)
  } finally {
    loadingEvents.value = false
  }
}

/**
 * One poll tick: the timeline and the run itself. The drawer shows the run's
 * status, output and completion time, which only a re-read of the run moves;
 * once it is terminal there is nothing more to wait for, so the poll stops
 * after one last timeline read.
 */
async function pollTick() {
  const run = crud.formModal.formData.value as CliRunDto | null
  if (!run?.id) {
    stopPolling()
    return
  }
  await reloadEvents()
  let fresh: CliRunDto
  try {
    fresh = await bridge.runs.get(run.id)
  } catch {
    // Keep what is on screen; the next tick retries.
    return
  }
  // The drawer may have moved on to another run, or closed, meanwhile.
  if ((crud.formModal.formData.value as CliRunDto | null)?.id !== run.id || fresh?.id !== run.id) return
  crud.formModal.formData.value = fresh
  if (CLI_RUN_TERMINAL_STATUSES.includes(fresh.status)) {
    stopPolling()
    await reloadEvents()
  }
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

/** Poll the open run while it is still going (one timer at most). */
function pollWhileLive() {
  if (pollTimer) return
  const status = (crud.formModal.formData.value as CliRunDto | null)?.status
  if (status && !CLI_RUN_TERMINAL_STATUSES.includes(status)) {
    pollTimer = setInterval(() => void pollTick(), LIVE_POLL_MS)
  }
}

watch(
  () => (crud.formModal.formData.value as CliRunDto | null)?.id,
  async (id) => {
    stopPolling()
    events.value = []
    eventsError.value = null
    if (!id) return

    await reloadEvents()
    // Another run may have been opened while this one's timeline loaded.
    if ((crud.formModal.formData.value as CliRunDto | null)?.id !== id) return
    pollWhileLive()
  },
)

onUnmounted(stopPolling)
// Kept alive in its tab, the page stays mounted while another page is on
// screen: no polling there. Shown again, the timeline catches up and the
// poll resumes if the drawer is still open on a running run.
onDeactivated(stopPolling)
onActivated(() => {
  if (pollTimer || !(crud.formModal.formData.value as CliRunDto | null)?.id) return
  void reloadEvents()
  pollWhileLive()
})

function eventTone(type: CliAgentEventType) {
  switch (type) {
    case CliAgentEventType.Error:
      return 'error'
    case CliAgentEventType.ToolUse:
    case CliAgentEventType.ToolResult:
      return 'info'
    case CliAgentEventType.Status:
      return 'success'
    default:
      return 'default'
  }
}

function eventTitle(event: CliRunMessageDto) {
  if (event.tool) return `${t(`eventType.${lowerFirst(event.type)}`)} · ${event.tool}`
  return t(`eventType.${lowerFirst(event.type)}`)
}

function lowerFirst(value: string) {
  return value.charAt(0).toLowerCase() + value.slice(1)
}

function detailItems(run: CliRunDto) {
  return [
    { label: t('columns.status'), value: t(runStatusBadgeMapping[run.status]?.label ?? '') },
    { label: t('columns.provider'), value: run.providerKey },
    { label: t('columns.duration'), value: formatRunDuration(run.durationMs) },
    { label: t('columns.cost'), value: formatRunCost(run.estimatedCostUsd) },
    { label: t('detail.failureReason'), value: run.failureReason },
    { label: t('detail.sessionId'), value: run.providerSessionId },
    { label: t('detail.workDirectory'), value: run.workDirectory },
    { label: t('detail.startedAt'), value: run.startedAt ? formatDateTime(run.startedAt) : null },
    {
      label: t('detail.completedAt'),
      value: run.completedAt ? formatDateTime(run.completedAt) : null,
    },
    { label: t('columns.prompt'), value: run.prompt },
    { label: t('detail.output'), value: run.output || EMPTY_DASH },
  ]
}

const rowActions: RowAction<CliRunDto>[] = [
  {
    key: 'cancel',
    label: 'actions.cancel',
    type: 'error',
    confirm: 'actions.cancelConfirm',
    // Cancelling a finished run is a no-op the backend rejects with 409;
    // hiding it keeps the row from offering something that cannot work. The
    // endpoint also needs ai.cliRun.execute, which a reader of the list lacks.
    show: (row) => can('ai.cliRun.execute') && !CLI_RUN_TERMINAL_STATUSES.includes(row.status),
    onClick: async (row) => {
      try {
        await bridge.runs.cancel(row.id)
        message.success(t('actions.cancelled'))
        await crud.refresh()
      } catch (error) {
        message.error(error instanceof Error ? error.message : String(error))
      }
    },
  },
]

const rowProps = (row: CliRunDto) => ({
  style: 'cursor: pointer',
  onClick: () => crud.openView(row),
})

</script>

<style scoped>
.cli-run-detail {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.cli-run-detail__error {
  display: flex;
  gap: 6px;
  align-items: flex-start;
  padding: 10px 12px;
  font-size: 13px;
  color: var(--tnzi-error);
  background: var(--tnzi-error-suppl, rgb(255 240 240));
  border-radius: var(--tnzi-admin-radius-md, 6px);
}

.cli-run-detail__section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.cli-run-detail__section-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-base-text-muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.cli-run-detail__event-body {
  margin: 0;
  font-size: 13px;
  white-space: pre-wrap;
}

.cli-run-detail__event-output {
  max-height: 200px;
  margin: 6px 0 0;
  overflow: auto;
  font-family: var(--tnzi-font-mono);
  font-size: 12px;
  white-space: pre-wrap;
}
</style>
