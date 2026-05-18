<template>
  <!-- Read-only page: showCreate=false hides the Create button; no-op handlers ensure
       useCrudPage's required callbacks are satisfied without being reachable from the UI. -->
  <TCrudPage
    :state="crud"
    :all-columns="accessLogColumns"
    :title="title"
    :translate="t"
    :show-create="false"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="accessLogFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { accessLogColumns, accessLogFormSchema } from './access-log-config'
import { translatePageKey } from '../_shared/translate'
import type { AccessLogInfoDto } from '@tnzi/core/services/system'

const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })

// No-op handlers: access logs are read-only; these callbacks are required by useCrudPage
// but the UI never triggers them because showCreate=false and row actions are view-only.
const readOnlyFn = async (): Promise<never> => { throw new Error('Access Log is read-only') }

const crud = useCrudPage<AccessLogInfoDto, string>({
  pageId: 'system.accessLogs',
  columns: accessLogColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.accessLogs.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: readOnlyFn,
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('system.accessLogs', key)
</script>
