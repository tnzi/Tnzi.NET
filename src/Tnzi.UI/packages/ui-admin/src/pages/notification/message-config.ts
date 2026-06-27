import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Aligned with backend NotificationInfo (Tnzi.Notification):
 *   id / type (NotificationType enum) / subject / content / isHtml /
 *   status (NotificationStatus enum) / sentTime / failureReason /
 *   retryCount / maxRetryCount / totalRecipientCount / successCount /
 *   failureCount / creationTime / priority / senderId / category /
 *   templateName / scheduledTime / recipients[] / attachments[]
 *
 * Earlier columns used speculative names (templateCode/recipient/channel/
 * sentAt/error) that never matched the backend payload — list showed blanks.
 */
interface NotificationMessageRow {
  id?: string
  type?: number          // NotificationType enum: 1=Email / 2=Sms / 3=InApp / 4=Webhook
  subject?: string
  status?: number        // NotificationStatus enum: 0=Pending / 1=Sending / 2=Sent / 3=Failed
  templateName?: string
  totalRecipientCount?: number
  successCount?: number
  failureCount?: number
  retryCount?: number
  sentTime?: string
  failureReason?: string
  creationTime?: string
}

function typeLabel(v?: number): string {
  switch (v) {
    case 1: return 'Email'
    case 2: return 'SMS'
    case 3: return 'InApp'
    case 4: return 'Webhook'
    default: return '—'
  }
}
function typeBadge(v?: number): 'info' | 'success' | 'warning' | 'default' {
  switch (v) {
    case 1: return 'info'
    case 2: return 'success'
    case 3: return 'warning'
    default: return 'default'
  }
}

function statusLabel(v?: number): string {
  switch (v) {
    case 0: return 'Pending'
    case 1: return 'Sending'
    case 2: return 'Sent'
    case 3: return 'Failed'
    default: return '—'
  }
}
function statusBadge(v?: number): 'warning' | 'info' | 'success' | 'error' | 'default' {
  switch (v) {
    case 0: return 'warning'
    case 1: return 'info'
    case 2: return 'success'
    case 3: return 'error'
    default: return 'default'
  }
}

export const notificationMessageColumns: ColumnDef<NotificationMessageRow>[] = [
  { key: 'subject', title: 'columns.subject', minWidth: 160, ellipsis: { tooltip: true } },
  {
    key: 'type',
    title: 'columns.type',
    width: 100,
    render: (row) =>
      h(TStatusBadge, { value: row.type ?? 0, type: typeBadge(row.type), label: typeLabel(row.type) }),
  },
  {
    key: 'status',
    title: 'columns.status',
    width: 100,
    render: (row) =>
      h(TStatusBadge, { value: row.status ?? 0, type: statusBadge(row.status), label: statusLabel(row.status) }),
  },
  { key: 'templateName', title: 'columns.templateName', minWidth: 140 },
  {
    key: 'recipients',
    title: 'columns.recipients',
    width: 130,
    render: (row) =>
      h(
        'span',
        { style: 'font-family: monospace; font-size: 12px' },
        `${row.successCount ?? 0}/${row.totalRecipientCount ?? 0}`,
      ),
  },
  { key: 'failureReason', title: 'columns.failureReason', minWidth: 160, ellipsis: { tooltip: true } },
  { key: 'retryCount', title: 'columns.retryCount', width: 90 },
  {
    key: 'sentTime',
    title: 'columns.sentTime',
    width: 150,
    render: (row) => h(TRelativeTime, { value: row.sentTime ?? row.creationTime }),
  },
]

export const notificationMessageFormSchema: FormSchemaItem[] = [
  { key: 'subject', labelKey: 'form.subject', label: 'Subject', type: 'text' },
  { key: 'templateName', labelKey: 'form.templateName', label: 'Template', type: 'text' },
  { key: 'content', labelKey: 'form.content', label: 'Content', type: 'textarea' },
  { key: 'failureReason', labelKey: 'form.failureReason', label: 'Failure Reason', type: 'textarea' },
]
