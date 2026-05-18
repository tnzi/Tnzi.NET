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
        :readonly="mode === 'view'"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t" />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TFormSchemaRenderer from '../_shared/form-schema'
import { useCrudPage } from '../../headless/useCrudPage'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import { featureColumns, featureFormSchema } from './feature-config'

interface FeatureDto {
  id?: string
  name?: string
  code?: string
  description?: string
  valueType?: 'boolean' | 'string' | 'number' | 'json'
  defaultValue?: string
  isEnabled?: boolean
}

const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<FeatureDto>({
  pageId: 'system.features',
  columns: featureColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.features.fetch(q),
  createData: (d) => bridge.features.create(d),
  updateData: (id, d) => bridge.features.update(String(id), d),
  deleteData: (ids) => bridge.features.delete(ids.map(String)),
})
crud.refresh().catch(() => undefined)

const title = 'title'
const t = (key: string) => translatePageKey('system.features', key)
</script>
