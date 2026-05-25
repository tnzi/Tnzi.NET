<template>
  <TCrudPage
    :state="crud"
    :all-columns="scheduledJobColumns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
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
    <template #rowActions="{ row }">
      <!-- "Trigger Now" lives in the per-row More menu, not the batch-action
           slot — admins almost always run a single job on demand, so hiding
           it behind a row selection was the wrong default. Confirm flow is
           delegated to TRowActions/useDialog. The Edit action is suppressed
           because Hangfire recurring jobs are registered in code via
           IBackgroundJobManager.CreateRecurring; the admin surface is
           list / trigger / delete only. -->
      <TRowActions
        :row="(row as ScheduledJobDto)"
        :state="crud"
        :translate="t"
        :show-edit="false"
        :more-options="moreOptionsFor"
        :on-more-select="onMoreSelect"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
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

function moreOptionsFor(row: ScheduledJobDto) {
  return [
    {
      key: 'trigger',
      label: t('actions.trigger'),
      disabled: row.removed === true,
    },
  ]
}

async function onMoreSelect(key: string, row: ScheduledJobDto): Promise<void> {
  if (key !== 'trigger' || !row.id) return
  try {
    await bridge.scheduledJobs.trigger(row.id)
    message.success(t('actions.triggerSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}
</script>
