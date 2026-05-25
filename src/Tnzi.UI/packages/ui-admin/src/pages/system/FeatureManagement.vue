<template>
  <TCrudPage
    :state="crud"
    :all-columns="featureColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="featureFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view' || ((formData as FeatureDto | null)?.isReadOnly ?? false)"
        :translate="t"
      />
    </template>
    <template #rowActions="{ row }">
      <!--
        Code-source rows (`isReadOnly`) ship from IFeatureDefinitionProvider —
        backend rejects edit/delete on them. Hide both actions so users don't
        click into a confusing server error.
      -->
      <TRowActions
        :row="(row as FeatureDto)"
        :state="crud"
        :translate="t"
        :show-edit="!(row as FeatureDto).isReadOnly"
        :show-delete="!(row as FeatureDto).isReadOnly"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TFormSchemaRenderer from '../_shared/form-schema'
import { useCrudPage } from '../../headless/useCrudPage'
import { createSystemBridge, type FeatureDto } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
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
crud.refresh().catch(() => undefined)

const title = 'title'
const t = (key: string) => translatePageKey('system.features', key)
</script>
