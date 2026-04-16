import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const loginLogColumns: ColumnDef[] = [
  { key: 'username',  title: 'Username' },
  { key: 'ip',        title: 'IP Address' },
  { key: 'userAgent', title: 'User Agent' },
  { key: 'success',   title: 'Success' },
  { key: 'loginAt',   title: 'Login At' },
  { key: 'location',  title: 'Location', visible: false },
]

export const loginLogFormSchema: FormSchemaItem[] = [
  { key: 'username',  label: 'Username',   type: 'text' },
  { key: 'ip',        label: 'IP Address', type: 'text' },
  { key: 'userAgent', label: 'User Agent', type: 'textarea' },
  { key: 'success',   label: 'Success',    type: 'switch' },
  { key: 'location',  label: 'Location',   type: 'text' },
  { key: 'loginAt',   label: 'Login At',   type: 'date' },
]
