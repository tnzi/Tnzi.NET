<template>
  <TCrudPage
    :state="crud"
    :all-columns="dictionaryColumns"
    :title="title"
    :translate="t"
  >
    <template #toolbarLeft>
      <NInput
        :value="groupPrefix"
        :placeholder="t('admin.shared.placeholder.filterByGroupPrefix')"
        clearable
        style="max-width: 240px"
        @update:value="onGroupPrefixChange"
      />
    </template>
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="dictionaryFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t" />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NInput } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { dictionaryColumns, dictionaryFormSchema } from './dictionary-config'
import { translatePageKey } from '../_shared/translate'
import type { SettingDto } from '@tnzi/core/services/system'

// Mapped to SettingDto — backend has no separate Dictionary entity.
const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<SettingDto, string>({
  pageId: 'system.dictionaries',
  columns: dictionaryColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.settings.fetch(query),
  createData: (data) => bridge.settings.create(data as never),
  updateData: (id, data) => bridge.settings.update(id, data as never),
  deleteData: (ids) => bridge.settings.delete(ids),
})

const groupPrefix = ref('')
function onGroupPrefixChange(v: string | null): void {
  groupPrefix.value = v ?? ''
  crud.setFilters({ groupPrefix: groupPrefix.value })
  crud.refresh().catch(() => undefined)
}

crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('system.dictionaries', key)
</script>
