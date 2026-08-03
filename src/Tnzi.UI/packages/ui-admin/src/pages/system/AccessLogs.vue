<template>
  <!-- Read-only page: createData/updateData/deleteData are omitted, so
       canCreate/canUpdate/canDelete are false and the shell hides all
       mutating affordances automatically. The per-row View action opens the
       read-only #detail drawer with the full access-log record (11 fields the
       table can't fit - geo/UA/request metadata). -->
  <TCrudPage
    :state="crud"
    :all-columns="accessLogColumns"
    :title="title"
    :translate="t"
    :detail-width="760"
    :detail-title="detailTitle"
    :row-actions="rowActions"
  >
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="accessLogFormSchema"
        :sections="accessLogFormSections"
        :model="(data ?? {}) as Record<string, unknown>"
        :readonly="true"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { viewAction, type RowAction } from '../../headless/row-actions'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { accessLogColumns, accessLogFormSchema, accessLogFormSections } from './access-log-config'
import { makePageTranslator } from '../_shared/translate'
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

// View-only detail - the schema renders in the #detail drawer (read-only).
const rowActions: RowAction<AccessLogInfoDto>[] = [viewAction(crud)]
const detailTitle = (row: AccessLogInfoDto): string => `${row.method ?? ''} ${row.path ?? ''}`.trim()

const t = makePageTranslator('system.accessLogs')
</script>
