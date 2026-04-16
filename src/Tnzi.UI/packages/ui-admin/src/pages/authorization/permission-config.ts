import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const permissionColumns: ColumnDef[] = [
  { key: 'code',        title: 'Code' },
  { key: 'name',        title: 'Name' },
  { key: 'moduleId',    title: 'Module' },
  { key: 'isEnabled',   title: 'Enabled' },
  { key: 'description', title: 'Description', visible: false },
]

export const permissionFormSchema: FormSchemaItem[] = [
  { key: 'code',        label: 'Code',        type: 'text', required: true },
  { key: 'name',        label: 'Name',        type: 'text', required: true },
  { key: 'moduleId',    label: 'Module',      type: 'text', required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
]
