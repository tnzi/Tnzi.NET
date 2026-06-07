<template>
  <!-- "Trigger Now" lives in the per-row actions — admins almost always run a
       single job on demand, so hiding it behind a row selection was the wrong
       default. The Edit action is suppressed (no editAction) because Hangfire
       recurring jobs are registered in code via
       IBackgroundJobManager.CreateRecurring; the admin surface is
       list / trigger / delete only. -->
  <TCrudPage
    :state="crud"
    :all-columns="scheduledJobColumns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="scheduledJobFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/rowActions'
import { createSystemBridge, type ScheduledJobDto } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { scheduledJobColumns, scheduledJobFormSchema } from './scheduled-job-config'
import { translatePageKey } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'

const title = 'title'
// Wired to Tnzi.Hangfire /admin/scheduled-jobs (2026-04-14). Client is
// injected by createTnziUiAdmin({ client }) at app bootstrap.
const bridge = createSystemBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('system.scheduledJobs', key)
const message = useSafeMessage()

const readOnlyFn = async (): Promise<never> => {
  // Hangfire recurring jobs are registered in code via
  // IBackgroundJobManager.CreateRecurring. The admin UI does not create/update
  // them — only list, trigger, and delete.
  throw new Error('ScheduledJob: create/update are not supported for Hangfire recurring jobs')
}

const crud = useCrudPage<ScheduledJobDto>({
  pageId: 'system.scheduledJobs',
  columns: scheduledJobColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.scheduledJobs.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: async (ids) => {
    for (const id of ids) {
      await bridge.scheduledJobs.delete(String(id))
    }
  },
})

crud.refresh().catch(() => undefined)

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

const rowActions: RowAction<ScheduledJobDto>[] = [
  {
    key: 'trigger',
    label: 'actions.trigger',
    disabled: (row) => row.removed === true,
    onClick: (row) => triggerJob(row),
  },
  deleteAction(crud),
]
</script>
