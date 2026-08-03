<template>
  <TItemPage
    :state="crud"
    :title="title"
    :translate="t"
    :detail-width="760"
    :detail-title="detailTitle"
    :show-create="false"
    show-batch
  >
    <template #item="{ item, selected, selectable, toggleSelect }">
      <TItemCard
        :title="item.id ?? EMPTY_DASH"
        icon="mdi:clock-outline"
        :icon-tone="stateTone(item.lastJobState ?? undefined)"
        :tags="jobTags(item)"
        :muted="item.removed === true"
        :selectable="selectable"
        :checked="selected"
        :selected="selected"
        clickable
        @update:checked="toggleSelect"
        @click="crud.openView(item)"
      >
        <template #meta>
          <div class="sj-meta">
            <span class="sj-meta__item">
              <TSvgIcon icon="mdi:calendar-clock" :size="13" />
              <code class="sj-cron">{{ item.cron || EMPTY_DASH }}</code>
            </span>
            <span v-if="item.queue" class="sj-meta__item">
              <TSvgIcon icon="mdi:tray-full" :size="13" />{{ item.queue }}
            </span>
            <span class="sj-meta__item">
              <TSvgIcon icon="mdi:history" :size="13" />{{ t('columns.lastExecution') }}
              <TRelativeTime :value="item.lastExecution" />
            </span>
            <span class="sj-meta__item">
              <TSvgIcon icon="mdi:play-circle-outline" :size="13" />{{ t('columns.nextExecution') }}
              <TRelativeTime :value="item.nextExecution" />
            </span>
          </div>
          <!-- A failing job says so on the row: the reason used to be reachable
               only by opening the detail drawer, so a red state chip was all the
               list ever showed. -->
          <p v-if="item.error" class="sj-error" :title="item.error">
            <TSvgIcon icon="mdi:alert-circle-outline" :size="13" />{{ item.error }}
          </p>
        </template>

        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :collapse="false" :translate="t" />
        </template>
      </TItemCard>
    </template>

    <!-- View opens the read-only detail drawer so a failed job's
         error / lastJobId / lastJobState are reachable in full. -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="scheduledJobFormSchema"
        :sections="scheduledJobFormSections"
        :model="(data ?? {}) as Record<string, unknown>"
        :readonly="true"
        :translate="t"
      />
    </template>
  </TItemPage>
</template>

<script setup lang="ts">
/**
 * Scheduled jobs as document rows, not a seven-column grid.
 *
 * A recurring job is read one at a time ("when does this run next, did the last
 * run fail, why"), never compared column-by-column. The table forced the cron
 * expression, the queue and both timestamps into narrow fixed columns and had
 * no room at all for the failure reason, which is the single field an admin
 * opens this page to see. The row card leads with the job id, carries its state
 * as a chip, and puts the error inline underneath.
 *
 * "Trigger Now" stays in the per-row actions: admins almost always run a single
 * job on demand, so hiding it behind a row selection was the wrong default.
 * There is no Edit action because Hangfire recurring jobs are registered in
 * code via IBackgroundJobManager.CreateRecurring; the admin surface is
 * list / trigger / view / delete only.
 */
import { TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TItemPage from '../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag } from '../../components/data/TItemCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, type RowAction } from '../../headless/row-actions'
import { createSystemBridge, type ScheduledJobDto } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { EMPTY_DASH } from '../../utils/placeholders'
import TFormSchemaRenderer from '../_shared/form-schema'
import {
  scheduledJobColumns,
  scheduledJobFormSchema,
  scheduledJobFormSections,
} from './scheduled-job-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'

const title = 'title'
// Wired to Tnzi.Hangfire /admin/scheduled-jobs (2026-04-14). Client is
// injected by createTnziUiAdmin({ client }) at app bootstrap.
const bridge = createSystemBridge({ client: useAdminClient() })
const t = makePageTranslator('system.scheduledJobs')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const crud = useCrudPage<ScheduledJobDto>({
  pageId: 'system.scheduledJobs',
  permission: 'system.scheduledJob',
  columns: scheduledJobColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.scheduledJobs.fetch(query),
  // Hangfire recurring jobs are registered in code via
  // IBackgroundJobManager.CreateRecurring - no create/update here; the admin
  // surface is list / trigger / delete only.
  deleteData: async (ids) => {
    for (const id of ids) {
      await bridge.scheduledJobs.delete(String(id))
    }
  },
})

async function triggerJob(row: ScheduledJobDto): Promise<void> {
  if (!row.id) return
  try {
    await bridge.scheduledJobs.trigger(row.id)
    message.success(t('actions.triggerSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// No View action: the row itself opens the detail drawer. Trigger and Delete are
// the two things you cannot express by opening a job.
const rowActions: RowAction<ScheduledJobDto>[] = [
  {
    key: 'trigger',
    label: 'actions.trigger',
    show: () => can('system.scheduledJob.execute'),
    disabled: (row) => row.removed === true,
    onClick: (row) => triggerJob(row),
  },
  deleteAction(crud),
]

const detailTitle = (row: ScheduledJobDto): string => row.id ?? ''

/** Tint the row glyph by the last run's outcome, so a failing job is visible
 *  while scanning rather than only after reading the chip. */
function stateTone(state?: string): 'default' | 'info' | 'success' | 'warning' | 'error' {
  switch (state?.toLowerCase()) {
    case 'succeeded': return 'success'
    case 'processing': return 'info'
    case 'failed': return 'error'
    case 'enqueued':
    case 'scheduled': return 'warning'
    default: return 'default'
  }
}

function jobTags(row: ScheduledJobDto): ItemCardTag[] {
  const tags: ItemCardTag[] = []
  if (row.lastJobState) tags.push({ label: row.lastJobState, type: stateTone(row.lastJobState) })
  // A removed job stays listed (its history is still useful) but must not read
  // as something that will run again.
  if (row.removed) tags.push({ label: t('columns.removed'), type: 'default' })
  return tags
}
</script>

<style scoped>
.sj-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
}
.sj-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}
.sj-cron {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11.5px;
  padding: 1px 6px;
  border-radius: 4px;
  background: var(--tnzi-bg-deep, #f6f8fa);
  color: var(--tnzi-base-text);
}
.sj-error {
  display: flex;
  align-items: flex-start;
  gap: 5px;
  margin: 4px 0 0;
  font-size: 12px;
  line-height: 1.45;
  color: var(--tnzi-error);
  /* One line in the list; the full stack trace lives in the detail drawer. */
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
