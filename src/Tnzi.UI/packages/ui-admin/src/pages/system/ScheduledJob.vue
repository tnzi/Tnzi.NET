<template>
  <TCrudPage
    :state="crud"
    :all-columns="scheduledJobColumns"
    :title="title"
    :translate="t"
  >
    <!-- Trigger now: available via batch-actions slot when exactly 1 row is selected -->
    <template #batchActions="{ selectedIds }">
      <NButton
        v-if="selectedIds.length === 1"
        type="warning"
        size="small"
        :loading="triggering"
        @click="triggerJob(String(selectedIds[0]))"
      >
        Trigger Now
      </NButton>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="scheduledJobFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createSystemBridge, type ScheduledJobDto } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { scheduledJobColumns, scheduledJobFormSchema } from './scheduled-job-config'
import { translatePageKey } from '../_shared/translate'

const title = 'Scheduled Jobs'
// Wired to Tnzi.Hangfire /admin/scheduled-jobs (2026-04-14). Client is
// injected by createTnziUiAdmin({ client }) at app bootstrap.
const bridge = createSystemBridge({ client: useAdminClient() })
const triggering = ref(false)

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

async function triggerJob(id: string): Promise<void> {
  triggering.value = true
  try {
    await bridge.scheduledJobs.trigger(id)
    await crud.refresh()
  } finally {
    triggering.value = false
  }
}

const t = (key: string) => translatePageKey('system.scheduledJobs', key)
</script>
