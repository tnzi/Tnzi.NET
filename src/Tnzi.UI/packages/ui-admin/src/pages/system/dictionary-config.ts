/**
 * Dictionary config - a Settings view scoped to the `Dictionary` group.
 *
 * There is no separate Dictionary entity: the backend exposes key/value
 * Settings, so this page maps to the settings bridge sub-contract with its
 * field names matching the real `SettingDto`. Dictionaries.vue hard-limits the
 * list + create to the dedicated `Dictionary` group so this page never overlaps
 * with Parameters (which shows every system setting).
 */
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { renderSettingValue } from './setting-value-cell'

/** Group every dictionary entry lives under (see Dictionaries.vue). */
export const DICTIONARY_GROUP = 'Dictionary'
/** List filter prefix - matches the dedicated group so parameters stay out. */
export const DICTIONARY_GROUP_PREFIX = 'Dict'

interface DictionaryRow {
  id?: string
  key?: string
  value?: string
  group?: string
  isSystem?: boolean
  sortOrder?: number
  description?: string
}

export const dictionaryColumns: ColumnDef<DictionaryRow>[] = [
  { key: 'key', title: 'columns.key', minWidth: 160 },
  { key: 'value', title: 'columns.value', minWidth: 180, render: (row) => renderSettingValue(row.value) },
  { key: 'group', title: 'columns.group', minWidth: 120 },
  { key: 'description', title: 'columns.description', minWidth: 160 },
  {
    key: 'isSystem',
    title: 'columns.isSystem',
    width: 110,
    render: (row) =>
      row.isSystem
        ? h(TStatusBadge, { value: 'system', type: 'info', label: 'System' })
        : h(TStatusBadge, { value: 'custom', type: 'default', label: 'Custom' }),
  },
]

export const dictionaryFormSchema: FormSchemaItem[] = [
  // The key is immutable once created (UpdateSettingDto has no Key), so typeFn
  // swaps it to the locked renderer (registered in Dictionaries.vue) on edit.
  {
    key: 'key',
    labelKey: 'form.key', label: 'Key',
    type: 'text',
    required: true,
    typeFn: (model) => (model.id ? 'dict-key-locked' : 'text'),
  },
  { key: 'value',       labelKey: 'form.value', label: 'Value',        type: 'text',     required: true },
  // `group` is intentionally absent: every dictionary entry is pinned to the
  // dedicated `Dictionary` group (injected on create by Dictionaries.vue) so
  // the page never overlaps Parameters. It stays visible in the list column.
  { key: 'description', labelKey: 'form.description', label: 'Description',  type: 'textarea' },
  { key: 'sortOrder',   labelKey: 'form.sortOrder', label: 'Sort',         type: 'number',   min: 0 },
]
