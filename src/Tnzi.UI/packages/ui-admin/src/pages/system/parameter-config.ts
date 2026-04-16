/**
 * Parameter config — Phase 3 Task 3.14.
 *
 * PLAN DEVIATION: The plan expected a ParameterService from @tnzi/core/services/system.
 * No such service exists. System settings (key/value pairs via useAdminSettingApi)
 * cover the "parameter" concept. Field names match real SettingDto backend fields.
 * valueType maps to the SettingValueType enum (0=String, 1=Integer, 2=Boolean, 3=Json).
 */
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const parameterColumns: ColumnDef[] = [
  { key: 'key',         title: 'Key' },
  { key: 'value',       title: 'Value' },
  { key: 'valueType',   title: 'Type' },
  { key: 'group',       title: 'Group' },
  { key: 'description', title: 'Description', visible: false },
]

// Value editor switches on the sibling `valueType` enum via typeFn:
//   0=String→text, 1=Integer→number, 2=Boolean→switch, 3=JSON→textarea.
// Default is 'text' when valueType is null/undefined (e.g. during create).
function valueEditorType(model: Record<string, unknown>): 'text' | 'number' | 'switch' | 'textarea' {
  switch (model.valueType) {
    case 1: return 'number'
    case 2: return 'switch'
    case 3: return 'textarea'
    default: return 'text'
  }
}

export const parameterFormSchema: FormSchemaItem[] = [
  { key: 'key',         label: 'Key',         type: 'text',   required: true },
  { key: 'valueType',   label: 'Type',         type: 'select', options: [
    { label: 'String',  value: 0 },
    { label: 'Integer', value: 1 },
    { label: 'Boolean', value: 2 },
    { label: 'JSON',    value: 3 },
  ] },
  { key: 'value',       label: 'Value',        type: 'text',   required: true, typeFn: valueEditorType },
  { key: 'group',       label: 'Group',        type: 'text' },
  { key: 'description', label: 'Description',  type: 'textarea' },
]
