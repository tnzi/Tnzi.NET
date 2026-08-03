<template>
  <TItemPage
    :state="crud"
    :title="title"
    :title-help="t('titleHelp')"
    :translate="t"
    show-batch
  >
    <template #item="{ item, selected, selectable, toggleSelect }">
      <TItemCard
        :title="item.key ?? EMPTY_DASH"
        :subtitle="item.description ?? undefined"
        icon="mdi:book-alphabet"
        :tags="item.isSystem ? [{ label: t('admin.shared.status.system'), type: 'info' }] : []"
        :selectable="selectable"
        :checked="selected"
        :selected="selected"
        clickable
        @update:checked="toggleSelect"
        @click="crud.openEdit(item)"
      >
        <!-- The value is what the reader came for, so it gets the right-hand
             slot a figure would occupy rather than a middle table column that
             truncated at whatever width was left over. -->
        <template #trailing>
          <code class="dict-value" :title="item.value ?? ''">{{ item.value || EMPTY_DASH }}</code>
        </template>
        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TItemCard>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="dictionaryFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :field-renderers="fieldRenderers"
      />
    </template>
  </TItemPage>
</template>

<script setup lang="ts">
/**
 * Dictionary entries as key/value rows.
 *
 * A dictionary entry is a pair, not a record with five attributes: the reader
 * scans keys down the left and reads the value on the right. The table gave the
 * value a middle column that truncated at whatever width was left after `group`
 * and `isSystem`, which is the wrong field to sacrifice on this page.
 *
 * Mapped to SettingDto - the backend has no separate Dictionary entity. The
 * page is hard-scoped to the dedicated `Dictionary` group so it never overlaps
 * with Parameters (which browses every system setting).
 */
import { h } from 'vue'
import { NInput } from 'naive-ui'
import TItemPage from '../../components/crud/TItemPage.vue'
import TItemCard from '../../components/data/TItemCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/row-actions'
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

// Hard-limit the list to the dedicated group prefix - the filter is fixed (no
// user-editable group input), so the page only ever shows dictionary entries.
crud.setFilters({ groupPrefix: DICTIONARY_GROUP_PREFIX })
crud.refresh().catch(() => undefined)

// No Edit action: the row itself opens the editor, so a second control doing
// exactly that would just compete with it for the same click.
const rowActions: RowAction<SettingDto>[] = [deleteAction(crud)]

// The key is immutable once created (UpdateSettingDto has no Key); the schema's
// typeFn swaps it to this locked renderer on edit.
const fieldRenderers: Record<string, FieldRenderer> = {
  'dict-key-locked': (ctx) => h(NInput, { value: (ctx.value as string) ?? '', disabled: true }),
}

const t = makePageTranslator('system.dictionaries')
</script>

<style scoped>
.dict-value {
  display: block;
  max-width: 320px;
  padding: 3px 8px;
  border-radius: 4px;
  background: var(--tnzi-bg-deep, #f6f8fa);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
@media (max-width: 660px) {
  .dict-value {
    max-width: 100%;
  }
}
</style>
