/**
 * Parameter config — same backend as Dictionary (`/admin/settings`, SettingDto).
 * The UI surface is the typed-parameter view: every row carries a `valueType`
 * (SettingValueType, serialized by the backend's global JsonStringEnumConverter
 * as its member name "String" | "Integer" | "Boolean" | "Json") and the value
 * editor switches its input shape on it.
 */
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

interface ParameterRow {
  id?: string
  key?: string
  value?: string
  valueType?: string
  group?: string
  description?: string
}

function valueTypeLabel(v?: string): string {
  // Wire value is the enum member name; only "Json" gets a nicer display cap.
  return v === 'Json' ? 'JSON' : (v ?? '—')
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
//   Integer→number, Boolean→switch, Json→json (TJsonEditor), else text.
// Default is 'text' when valueType is null/undefined (e.g. during create).
function valueEditorType(model: Record<string, unknown>): 'text' | 'number' | 'switch' | 'json' {
  switch (model.valueType) {
    case 'Integer': return 'number'
    case 'Boolean': return 'switch'
    case 'Json': return 'json'
    default: return 'text'
  }
}

export const parameterFormSchema: FormSchemaItem[] = [
  // The setting key is immutable once created (UpdateSettingDto has no Key), so
  // typeFn swaps it to the locked renderer (registered in Parameters.vue) when
  // the model already has an id — new rows keep the editable text input.
  {
    key: 'key',
    labelKey: 'form.key', label: 'Key',
    type: 'text',
    required: true,
    typeFn: (model) => (model.id ? 'param-key-locked' : 'text'),
  },
  {
    key: 'valueType',
    labelKey: 'form.valueType', label: 'Type',
    type: 'select',
    required: true,
    // SettingValueType member names (JsonStringEnumConverter wire shape).
    options: [
      { label: 'String', value: 'String' },
      { label: 'Integer', value: 'Integer' },
      { label: 'Boolean', value: 'Boolean' },
      { label: 'JSON', value: 'Json' },
    ],
  },
  { key: 'value', labelKey: 'form.value', label: 'Value', type: 'text', required: true, typeFn: valueEditorType },
  { key: 'group', labelKey: 'form.group', label: 'Group', type: 'text' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
]
