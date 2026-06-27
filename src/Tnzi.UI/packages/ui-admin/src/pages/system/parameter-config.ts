/**
 * Parameter config — same backend as Dictionary (`/admin/settings`, SettingDto)
 * but the UI surface is the typed-parameter view: every row has a `valueType`
 * (SettingValueType enum) and the value editor switches input shape on it.
 *
 * `valueType` is rendered as a human label rather than the raw int.
 * SettingValueType: 0=String, 1=Integer, 2=Boolean, 3=Json.
 */
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

interface ParameterRow {
  id?: string
  key?: string
  value?: string
  valueType?: number
  group?: string
  description?: string
}

function valueTypeLabel(v?: number): string {
  switch (v) {
    case 0: return 'String'
    case 1: return 'Integer'
    case 2: return 'Boolean'
    case 3: return 'JSON'
    default: return '—'
  }
}

export const parameterColumns: ColumnDef<ParameterRow>[] = [
  { key: 'key', title: 'columns.key', minWidth: 160 },
  { key: 'value', title: 'columns.value', minWidth: 180 },
  {
    key: 'valueType',
    title: 'columns.valueType',
    width: 110,
    render: (row) =>
      h(
        'span',
        { style: 'font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px' },
        valueTypeLabel(row.valueType),
      ),
  },
  { key: 'group', title: 'columns.group', minWidth: 120 },
  { key: 'description', title: 'columns.description', visible: false },
]

// Value editor switches on the sibling `valueType` enum via typeFn:
//   0=String→text, 1=Integer→number, 2=Boolean→switch, 3=JSON→json (TJsonEditor).
// Default is 'text' when valueType is null/undefined (e.g. during create).
function valueEditorType(model: Record<string, unknown>): 'text' | 'number' | 'switch' | 'json' {
  switch (model.valueType) {
    case 1: return 'number'
    case 2: return 'switch'
    case 3: return 'json'
    default: return 'text'
  }
}

export const parameterFormSchema: FormSchemaItem[] = [
  { key: 'key', labelKey: 'form.key', label: 'Key', type: 'text', required: true },
  {
    key: 'valueType',
    labelKey: 'form.valueType', label: 'Type',
    type: 'select',
    required: true,
    options: [
      { label: 'String', value: 0 },
      { label: 'Integer', value: 1 },
      { label: 'Boolean', value: 2 },
      { label: 'JSON', value: 3 },
    ],
  },
  { key: 'value', labelKey: 'form.value', label: 'Value', type: 'text', required: true, typeFn: valueEditorType },
  { key: 'group', labelKey: 'form.group', label: 'Group', type: 'text' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
]
