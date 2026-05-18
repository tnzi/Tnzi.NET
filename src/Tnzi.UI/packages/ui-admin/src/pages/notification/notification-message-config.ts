import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

interface NotificationMessageRow {
  id?: string
  templateCode?: string
  recipient?: string
  channel?: 'email' | 'sms' | 'push' | 'webhook' | string
  status?: 'pending' | 'sent' | 'failed' | 'cancelled' | string
  sentAt?: string
  error?: string
}

const CHANNEL_TYPE: Record<string, 'info' | 'success' | 'warning' | 'error' | 'default'> = {
  email: 'info',
  sms: 'success',
  push: 'warning',
  webhook: 'default',
}

const STATUS_TYPE: Record<string, 'info' | 'success' | 'warning' | 'error' | 'default'> = {
  pending: 'warning',
  sent: 'success',
  failed: 'error',
  cancelled: 'default',
}

export const notificationMessageColumns: ColumnDef<NotificationMessageRow>[] = [
  { key: 'recipient', title: 'columns.recipient', width: 220, fixed: 'left' },
  { key: 'templateCode', title: 'columns.templateCode', width: 180 },
  {
    key: 'channel',
    title: 'columns.channel',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.channel ?? 'unknown',
        type: CHANNEL_TYPE[row.channel ?? ''] ?? 'default',
        label: row.channel ?? '—',
      }),
  },
  {
    key: 'status',
    title: 'columns.status',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.status ?? 'unknown',
        type: STATUS_TYPE[row.status ?? ''] ?? 'default',
        label: row.status ?? '—',
      }),
  },
  { key: 'error', title: 'columns.error' },
  {
    key: 'sentAt',
    title: 'columns.sentAt',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.sentAt }),
  },
]

export const notificationMessageFormSchema: FormSchemaItem[] = [
  { key: 'templateCode', label: 'Template',  type: 'text' },
  { key: 'recipient',    label: 'Recipient', type: 'text' },
  { key: 'channel',      label: 'Channel',   type: 'text' },
  { key: 'status',       label: 'Status',    type: 'text' },
  { key: 'payload',      label: 'Payload',   type: 'textarea' },
  { key: 'error',        label: 'Error',     type: 'textarea' },
]
