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
  { key: 'key', title: 'columns.key', width: 220, fixed: 'left' },
  { key: 'value', title: 'columns.value', width: 260 },
  { key: 'group', title: 'columns.group', width: 160 },
  { key: 'description', title: 'columns.description' },
  {
    key: 'isSystem',
    title: 'columns.isSystem',
    width: 110,
    fixed: 'right',
    render: (row) =>
      row.isSystem
        ? h(TStatusBadge, { value: 'system', type: 'info', label: 'System' })
        : h(TStatusBadge, { value: 'custom', type: 'default', label: 'Custom' }),
  },
]

export const dictionaryFormSchema: FormSchemaItem[] = [
  { key: 'key',         label: 'Key',         type: 'text',     required: true },
  { key: 'value',       label: 'Value',        type: 'text',     required: true },
  { key: 'group',       label: 'Group',        type: 'text' },
  { key: 'description', label: 'Description',  type: 'textarea' },
  { key: 'sortOrder',   label: 'Sort',         type: 'number',   min: 0 },
]
