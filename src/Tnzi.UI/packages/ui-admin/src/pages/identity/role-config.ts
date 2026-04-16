import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const roleColumns: ColumnDef[] = [
  { key: 'name',        title: 'Name' },
  { key: 'code',        title: 'Code' },
  { key: 'enabled',     title: 'Enabled' },
  { key: 'description', title: 'Description' },
  { key: 'createdAt',   title: 'Created At', visible: false },
]

export const roleFormSchema: FormSchemaItem[] = [
  { key: 'name',        label: 'Name',        type: 'text',     required: true },
  { key: 'code',        label: 'Code',        type: 'text',     required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'enabled',     label: 'Enabled',     type: 'switch' },
]
