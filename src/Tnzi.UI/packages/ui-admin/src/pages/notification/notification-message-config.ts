import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const notificationMessageColumns: ColumnDef[] = [
  { key: 'templateCode', title: 'Template' },
  { key: 'recipient',    title: 'Recipient' },
  { key: 'channel',      title: 'Channel' },
  { key: 'status',       title: 'Status' },
  { key: 'sentAt',       title: 'Sent At' },
  { key: 'error',        title: 'Error', visible: false },
]

export const notificationMessageFormSchema: FormSchemaItem[] = [
  { key: 'templateCode', label: 'Template',  type: 'text' },
  { key: 'recipient',    label: 'Recipient', type: 'text' },
  { key: 'channel',      label: 'Channel',   type: 'text' },
  { key: 'status',       label: 'Status',    type: 'text' },
  { key: 'payload',      label: 'Payload',   type: 'textarea' },
  { key: 'error',        label: 'Error',     type: 'textarea' },
]
