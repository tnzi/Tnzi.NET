import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const functionModuleColumns: ColumnDef[] = [
  { key: 'code',      title: 'Code' },
  { key: 'name',      title: 'Name' },
  { key: 'parentId',  title: 'Parent' },
  { key: 'order',     title: 'Sort' },
  { key: 'isEnabled', title: 'Enabled' },
]

export const functionModuleFormSchema: FormSchemaItem[] = [
  { key: 'code',      label: 'Code',      type: 'text',   required: true },
  { key: 'name',      label: 'Name',      type: 'text',   required: true },
  { key: 'parentId',  label: 'Parent ID', type: 'text' },
  { key: 'order',     label: 'Sort',      type: 'number', min: 0 },
  { key: 'isEnabled', label: 'Enabled',   type: 'switch' },
]
