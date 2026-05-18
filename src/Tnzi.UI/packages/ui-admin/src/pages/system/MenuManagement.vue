<template>
  <TCrudPage
    :state="crud"
    :all-columns="menuColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="menuFormSchema"
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
import { useCrudPage } from '../../headless/useCrudPage'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { menuColumns, menuFormSchema } from './menu-config'
import { translatePageKey } from '../_shared/translate'
import type { MenuInfoDto } from '@tnzi/core/services/system'

const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<MenuInfoDto, string>({
  pageId: 'system.menus',
  columns: menuColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.menus.fetch(query),
  createData: (data) => bridge.menus.create(data as never),
  updateData: (id, data) => bridge.menus.update(id, data as never),
  deleteData: (ids) => bridge.menus.delete(ids),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('system.menus', key)
</script>
