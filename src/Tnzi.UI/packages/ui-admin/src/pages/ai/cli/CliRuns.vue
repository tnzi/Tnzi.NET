<template>
  <TCrudPage
    :search-fields="runSearchFields"
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

          <TEmpty v-if="!events.length && !loadingEvents" :text="t('detail.noEvents')" />

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
import { onUnmounted, ref, watch } from 'vue'
import { NButton, NTimeline, NTimelineItem } from 'naive-ui'
import { formatDateTime } from '@tnzi/core'
import { EMPTY_DASH, TDescriptions, TEmpty, TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { type RowAction } from '../../../headless/row-actions'
import { useSafeMessage } from '../../_shared/safe-message'
import { makePageTranslator } from '../../../i18n/translate'
import { useAdminClient } from '../../../plugin/client'
import {
  createCliAgentBridge,
  CliAgentEventType,
  CliRunStatus,
  CLI_RUN_TERMINAL_STATUSES,
  type CliRunDto,
  type CliRunMessageDto,
  type CliRunQueryDto,
} from '../../../services/bridges/cli-agent-bridge'
import { runColumns, runSearchFields, runStatusBadgeMapping } from './cli-runtime-config'

/** How often a still-running run's timeline is re-fetched while its drawer is open. */
const LIVE_POLL_MS = 3000

const bridge = createCliAgentBridge({ client: useAdminClient() })
const message = useSafeMessage()
const t = makePageTranslator('ai.cliRuns')
const title = 'CLI Runs'

const events = ref<CliRunMessageDto[]>([])
const loadingEvents = ref(false)
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
  fetchData: (query) =>
    bridge.runs.list({
      pageIndex: query.pageIndex,
      pageSize: query.pageSize,
      ...(query.filters ?? {}),
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
    events.value = await bridge.runs.messages(run.id)
  } finally {
    loadingEvents.value = false
  }
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

watch(
  () => (crud.formModal.formData.value as CliRunDto | null)?.id,
  async (id) => {
    stopPolling()
    if (!id) {
      events.value = []
      return
    }

    await reloadEvents()

    const status = (crud.formModal.formData.value as CliRunDto | null)?.status
    if (status && !CLI_RUN_TERMINAL_STATUSES.includes(status)) {
      pollTimer = setInterval(reloadEvents, LIVE_POLL_MS)
    }
  },
)

onUnmounted(stopPolling)

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
    { label: t('columns.duration'), value: run.durationMs ? `${run.durationMs} ms` : null },
    {
      label: t('columns.cost'),
      value: run.estimatedCostUsd != null ? `$${run.estimatedCostUsd.toFixed(4)}` : null,
    },
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
    // hiding it keeps the row from offering something that cannot work.
    show: (row) => !CLI_RUN_TERMINAL_STATUSES.includes(row.status),
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

// Referenced by the template's status badge mapping lookup.
void CliRunStatus
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
  color: var(--tnzi-text-3);
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
