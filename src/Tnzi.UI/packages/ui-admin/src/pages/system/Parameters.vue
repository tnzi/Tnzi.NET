<template>
  <TCrudPage
    :state="crud"
    :all-columns="parameterColumns"
    :title="title"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #toolbarLeft>
      <NInput
        :value="groupPrefix"
        :placeholder="t('admin.shared.placeholder.filterByGroupPrefix')"
        clearable
        class="max-w-240px"
              size="small"
        @update:value="onGroupPrefixChange"
      />
    </template>
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="parameterFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :field-renderers="fieldRenderers"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { h, ref } from 'vue'
import { NInput } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { type FieldRenderer } from '../_shared/form-schema'
import { parameterColumns, parameterFormSchema } from './parameter-config'
import { makePageTranslator } from '../_shared/translate'
import type { SettingDto } from '@tnzi/core/services/system'

// Mapped to SettingDto — no separate Parameter entity in the backend.
const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<SettingDto, string>({
  pageId: 'system.parameters',
  columns: parameterColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.settings.fetch(query),
  createData: (data) => bridge.settings.create(data as never),
  updateData: (id, data) => bridge.settings.update(id, data as never),
  deleteData: (ids) => bridge.settings.delete(ids),
})

const rowActions: RowAction<SettingDto>[] = [editAction(crud), deleteAction(crud)]

// The key is immutable once the setting exists (UpdateSettingDto has no Key).
// parameter-config's typeFn swaps the key field to this locked renderer in edit
// mode so the value is still visible but not editable.
const fieldRenderers: Record<string, FieldRenderer> = {
  'param-key-locked': (ctx) => h(NInput, { value: (ctx.value as string) ?? '', disabled: true }),
}

const groupPrefix = ref('')
function onGroupPrefixChange(v: string | null): void {
  groupPrefix.value = v ?? ''
  crud.setFilters({ groupPrefix: groupPrefix.value })
  crud.refresh().catch(() => undefined)
}

const t = makePageTranslator('system.parameters')
</script>
