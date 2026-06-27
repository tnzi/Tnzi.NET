/**
 * Dictionary config — Phase 3 Task 3.13.
 *
 * PLAN DEVIATION: The plan expected a DictionaryService from @tnzi/core/services/system.
 * No such service exists. The real system module exposes Settings (key/value pairs),
 * which is functionally equivalent. This page maps to the settings bridge sub-contract.
 * Field names match the real SettingDto backend fields.
 */
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

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
  { key: 'value', title: 'columns.value', minWidth: 180 },
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
  { key: 'key',         labelKey: 'form.key', label: 'Key',         type: 'text',     required: true },
  { key: 'value',       labelKey: 'form.value', label: 'Value',        type: 'text',     required: true },
  { key: 'group',       labelKey: 'form.group', label: 'Group',        type: 'text' },
  { key: 'description', labelKey: 'form.description', label: 'Description',  type: 'textarea' },
  { key: 'sortOrder',   labelKey: 'form.sortOrder', label: 'Sort',         type: 'number',   min: 0 },
]
