<template>
  <TCrudPage
    :state="crud"
    :all-columns="dictionaryColumns"
    :title="title"
    :title-help="t('titleHelp')"
    :translate="t"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="dictionaryFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :field-renderers="fieldRenderers"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { h } from 'vue'
import { NInput } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { type FieldRenderer } from '../_shared/form-schema'
import {
  dictionaryColumns,
  dictionaryFormSchema,
  DICTIONARY_GROUP,
  DICTIONARY_GROUP_PREFIX,
} from './dictionary-config'
import { makePageTranslator } from '../_shared/translate'
import type { SettingDto } from '@tnzi/core/services/system'

// Mapped to SettingDto — backend has no separate Dictionary entity. The page is
// hard-scoped to the dedicated `Dictionary` group so it never overlaps with
// Parameters (which browses every system setting).
const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<SettingDto, string>({
  pageId: 'system.dictionaries',
  // Route gates on system.parameter.view (shared /admin/settings endpoint), so
  // the write codes derive from the same base the backend actually enforces.
  permission: 'system.parameter',
  columns: dictionaryColumns,
  rowKey: (r) => r.id,
  // autoLoad off so the fixed group filter is set before the first fetch.
  autoLoad: false,
  fetchData: (query) => bridge.settings.fetch(query),
  // Pin every new entry to the dedicated dictionary group.
  createData: (data) => bridge.settings.create({ ...data, group: DICTIONARY_GROUP } as never),
  updateData: (id, data) => bridge.settings.update(id, data as never),
  deleteData: (ids) => bridge.settings.delete(ids),
})

// Hard-limit the list to the dedicated group prefix — the filter is fixed (no
// user-editable group input), so the page only ever shows dictionary entries.
crud.setFilters({ groupPrefix: DICTIONARY_GROUP_PREFIX })
crud.refresh().catch(() => undefined)

const rowActions: RowAction<SettingDto>[] = [editAction(crud), deleteAction(crud)]

// The key is immutable once created (UpdateSettingDto has no Key); the schema's
// typeFn swaps it to this locked renderer on edit.
const fieldRenderers: Record<string, FieldRenderer> = {
  'dict-key-locked': (ctx) => h(NInput, { value: (ctx.value as string) ?? '', disabled: true }),
}

const t = makePageTranslator('system.dictionaries')
</script>
