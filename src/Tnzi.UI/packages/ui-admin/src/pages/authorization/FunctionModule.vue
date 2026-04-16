<template>
  <TCrudPage
    :state="crud"
    :all-columns="functionModuleColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="functionModuleFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { functionModuleColumns, functionModuleFormSchema } from './function-module-config'
import { translatePageKey } from '../_shared/translate'

interface FunctionModuleDto { id: string; code: string; name: string; parentId?: string; order: number; isEnabled: boolean }

const title = 'Function Module Management'
const bridge = createAuthorizationBridge({ client: useAdminClient() })

const crud = useCrudPage<FunctionModuleDto>({
  pageId: 'authorization.functionModules',
  columns: functionModuleColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.functionModules.fetch(query),
  createData: (data) => bridge.functionModules.create(data as never) as Promise<FunctionModuleDto>,
  updateData: (id, data) => bridge.functionModules.update(String(id), data as never) as Promise<FunctionModuleDto>,
  deleteData: (ids) => bridge.functionModules.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('authorization.functionModules', key)
</script>
