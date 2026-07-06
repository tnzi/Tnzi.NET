<template>
  <TCrudPage
    :state="crud"
    :all-columns="featureColumns"
    :title="title"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="featureFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view' || ((formData as FeatureDto | null)?.isReadOnly ?? false)"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TFormSchemaRenderer from '../_shared/form-schema'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createSystemBridge, type FeatureDto } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { featureColumns, featureFormSchema } from './feature-config'

const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<FeatureDto>({
  pageId: 'system.features',
  columns: featureColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.features.fetch(q),
  createData: (d) => bridge.features.create(d as never),
  updateData: (id, d) => bridge.features.update(String(id), d as never),
  deleteData: (ids) => bridge.features.delete(ids.map(String)),
})

// Code-source rows (`isReadOnly`) ship from IFeatureDefinitionProvider — backend
// rejects edit/delete on them. Hide both actions so users don't click into a
// confusing server error.
const rowActions: RowAction<FeatureDto>[] = [
  editAction(crud, { show: (row) => !row.isReadOnly }),
  deleteAction(crud, { show: (row) => !row.isReadOnly }),
]

const title = 'title'
const t = makePageTranslator('system.features')
</script>
