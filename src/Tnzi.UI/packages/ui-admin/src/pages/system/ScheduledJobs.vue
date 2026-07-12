<template>
  <!-- "Trigger Now" lives in the per-row actions — admins almost always run a
       single job on demand, so hiding it behind a row selection was the wrong
       default. The Edit action is suppressed (no editAction) because Hangfire
       recurring jobs are registered in code via
       IBackgroundJobManager.CreateRecurring; the admin surface is
       list / trigger / view / delete only. -->
  <TCrudPage
    :state="crud"
    :all-columns="scheduledJobColumns"
    :title="title"
    :translate="t"
    :detail-width="760"
    :detail-title="detailTitle"
    :row-actions="rowActions"
    :row-actions-collapse="false"
  >
    <!-- View opens the read-only detail drawer so a failed job's
         error / lastJobId / lastJobState are reachable (the table omits them). -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="scheduledJobFormSchema"
        :model="(data ?? {}) as Record<string, unknown>"
        :readonly="true"
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, viewAction, type RowAction } from '../../headless/rowActions'
import { createSystemBridge, type ScheduledJobDto } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { scheduledJobColumns, scheduledJobFormSchema } from './scheduled-job-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'

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
  // IBackgroundJobManager.CreateRecurring — no create/update here; the admin
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

const rowActions: RowAction<ScheduledJobDto>[] = [
  {
    key: 'trigger',
    label: 'actions.trigger',
    show: () => can('system.scheduledJob.execute'),
    disabled: (row) => row.removed === true,
    onClick: (row) => triggerJob(row),
  },
  viewAction(crud),
  deleteAction(crud),
]

const detailTitle = (row: ScheduledJobDto): string => row.id ?? ''
</script>
