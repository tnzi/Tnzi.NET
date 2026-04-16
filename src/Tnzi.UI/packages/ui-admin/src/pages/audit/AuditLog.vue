<template>
  <!-- Read-only page: showCreate=false hides the Create button; no-op handlers ensure
       useCrudPage's required callbacks are satisfied without being reachable from the UI. -->
  <TCrudPage
    :state="crud"
    :all-columns="auditLogColumns"
    :title="title"
    :translate="t"
    :show-create="false"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="auditLogFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createAuditBridge } from '../../services/bridges/audit-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { auditLogColumns, auditLogFormSchema } from './audit-log-config'
import { translatePageKey } from '../_shared/translate'
import type { AuditOperationDto } from '@tnzi/core/services/audit'

const title = 'Audit Log'
const bridge = createAuditBridge({ client: useAdminClient() })

// No-op handlers: audit logs are read-only; these callbacks are required by useCrudPage
// but the UI never triggers them because showCreate=false and row actions are view-only.
const readOnlyFn = async (): Promise<never> => { throw new Error('Audit Log is read-only') }

const crud = useCrudPage<AuditOperationDto>({
  pageId: 'audit.logs',
  columns: auditLogColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.logs.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: readOnlyFn,
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('audit.logs', key)
</script>
