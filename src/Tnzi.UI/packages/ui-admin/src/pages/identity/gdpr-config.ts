import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const gdprColumns: ColumnDef[] = [
  { key: 'userId',      title: 'User ID' },
  { key: 'username',    title: 'Username' },
  { key: 'requestType', title: 'Type' },
  { key: 'status',      title: 'Status' },
  { key: 'requestedAt', title: 'Requested At' },
]

export const gdprFormSchema: FormSchemaItem[] = [
  { key: 'userId',      label: 'User ID',    type: 'text' },
  { key: 'requestType', label: 'Type',       type: 'select', options: [
    { label: 'Export',   value: 'export' },
    { label: 'Deletion', value: 'deletion' },
  ] },
  { key: 'notes',       label: 'Notes',      type: 'textarea' },
]
