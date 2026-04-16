/**
 * Dictionary config — Phase 3 Task 3.13.
 *
 * PLAN DEVIATION: The plan expected a DictionaryService from @tnzi/core/services/system.
 * No such service exists. The real system module exposes Settings (key/value pairs),
 * which is functionally equivalent. This page maps to the settings bridge sub-contract.
 * Field names match the real SettingDto backend fields.
 */
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const dictionaryColumns: ColumnDef[] = [
  { key: 'key',         title: 'Key' },
  { key: 'value',       title: 'Value' },
  { key: 'group',       title: 'Group' },
  { key: 'isSystem',    title: 'System' },
  { key: 'sortOrder',   title: 'Sort', visible: false },
]

export const dictionaryFormSchema: FormSchemaItem[] = [
  { key: 'key',         label: 'Key',         type: 'text',     required: true },
  { key: 'value',       label: 'Value',        type: 'text',     required: true },
  { key: 'group',       label: 'Group',        type: 'text' },
  { key: 'description', label: 'Description',  type: 'textarea' },
  { key: 'sortOrder',   label: 'Sort',         type: 'number',   min: 0 },
]
