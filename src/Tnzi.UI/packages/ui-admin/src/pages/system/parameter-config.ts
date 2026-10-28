/**
 * Parameter config - same backend as Dictionary (`/admin/settings`, SettingDto).
 * The UI surface is the typed-parameter view: every row carries a `valueType`
 * (SettingValueType, serialized by the backend's global JsonStringEnumConverter
 * as its member name "String" | "Integer" | "Boolean" | "Json") and the value
 * editor switches its input shape on it.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import { NInputNumber, NSwitch } from 'naive-ui'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FieldRenderer, FormSchemaItem } from '../_shared/form-schema'
import { renderSettingValue } from './setting-value-cell'

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
  return v === 'Json' ? 'JSON' : (v ?? EMPTY_DASH)
}

export const parameterColumns: ColumnDef<ParameterRow>[] = [
  { key: 'key', title: 'columns.key', minWidth: 160 },
  { key: 'value', title: 'columns.value', minWidth: 180, render: (row) => renderSettingValue(row.value) },
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
// The number and switch editors are the page's own (`parameterFieldRenderers`
// below): the value is a string whatever its type.
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
  // the model already has an id - new rows keep the editable text input.
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

/** A Boolean setting is on when its stored string says `true`, in any case (as the server parses it). */
function isOn(raw: unknown): boolean {
  return raw === true || String(raw ?? '').trim().toLowerCase() === 'true'
}

/** The stored string of an Integer setting as a number, or empty when it is not a whole number. */
function toWholeNumber(raw: unknown): number | null {
  const text = String(raw ?? '').trim()
  if (text === '') return null
  const n = Number(text)
  return Number.isInteger(n) ? n : null
}

/**
 * Editors for a typed value. `Value` is a string on the wire for every value
 * type (`CreateSettingDto` / `UpdateSettingDto`), so the form keeps the string
 * and these controls convert at their own edge: a stored `'true'` shows ON, and
 * the answer goes back as `'true'` / `'false'` / `'42'`. A JSON bool or number
 * in the body fails model binding with 400.
 *
 * They are registered under the builtin names the value field's `typeFn`
 * returns, so the form keeps the builtin semantics (a switch has no missing
 * state; a number keeps the required check). The value field is the only
 * `number` or `switch` field in `parameterFormSchema`.
 */
export const parameterFieldRenderers: Record<string, FieldRenderer> = {
  switch: (ctx) =>
    h(NSwitch, {
      value: isOn(ctx.value),
      disabled: ctx.readonly,
      'onUpdate:value': (v: boolean) => ctx.onUpdate(v ? 'true' : 'false'),
    }),
  number: (ctx) =>
    h(NInputNumber, {
      value: toWholeNumber(ctx.value),
      readonly: ctx.readonly,
      precision: 0,
      'onUpdate:value': (v: number | null) => ctx.onUpdate(v == null ? '' : String(v)),
    }),
}

/**
 * The body a save sends. A Boolean parameter whose switch was never touched
 * holds no value while the switch shows OFF; it is sent as `'false'`, which is
 * what the operator saw (`Value` is required, so leaving it out is a 400).
 */
export function toParameterWrite<T extends { value?: unknown; valueType?: unknown }>(data: T): T {
  if (data.valueType === 'Boolean') return { ...data, value: isOn(data.value) ? 'true' : 'false' }
  return data
}
