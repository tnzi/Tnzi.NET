<template>
  <!-- Read-only page: createData/updateData/deleteData are omitted, so
       canCreate/canUpdate/canDelete are false and the shell hides all
       mutating affordances automatically. -->
  <TCrudPage
    :state="crud"
    :all-columns="accessLogColumns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="accessLogFormSchema"
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
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { accessLogColumns, accessLogFormSchema } from './access-log-config'
import { translatePageKey } from '../_shared/translate'
import type { AccessLogInfoDto } from '@tnzi/core/services/system'

const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<AccessLogInfoDto, string>({
  pageId: 'system.accessLogs',
  columns: accessLogColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.accessLogs.fetch(query),
  // read-only: no create/update/delete callbacks → affordances auto-hidden
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('system.accessLogs', key)
</script>
