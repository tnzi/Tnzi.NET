import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'
import {
  NotificationType,
  NotificationStatus,
  getNotificationTypeLabel,
  getNotificationStatusLabel,
} from '@tnzi/core/services/notification'

/**
 * Aligned with backend NotificationInfo (Tnzi.Notification):
 *   id / type (NotificationType enum) / subject / content / isHtml /
 *   status (NotificationStatus enum) / sentTime / failureReason /
 *   retryCount / maxRetryCount / totalRecipientCount / successCount /
 *   failureCount / creationTime / priority / senderId / category /
 *   templateName / scheduledTime / recipients[] / attachments[]
 *
 * Both enums now serialize as their PascalCase member name strings (global
 * JsonStringEnumConverter). The old hand-written numeric switch mismatched the
 * backend (mapped 3→InApp / 4→Webhook, values NotificationType does not have),
 * so type/status badges are now derived through the shared @tnzi/core label
 * helpers + enum-member badge tones.
 */
interface NotificationMessageRow {
  id?: string
  type?: NotificationType
  subject?: string
  status?: NotificationStatus
  templateName?: string
  totalRecipientCount?: number
  successCount?: number
  failureCount?: number
  retryCount?: number
  sentTime?: string
  failureReason?: string
  creationTime?: string
}

function typeLabel(v?: NotificationType): string {
  return v != null ? getNotificationTypeLabel(v) : '—'
}
function typeBadge(v?: NotificationType): 'info' | 'success' | 'warning' | 'default' {
  switch (v) {
    case NotificationType.Email: return 'info'
    case NotificationType.Sms: return 'success'
    case NotificationType.Push: return 'warning'
    default: return 'default'
  }
}

function statusLabel(v?: NotificationStatus): string {
  return v != null ? getNotificationStatusLabel(v) : '—'
}
function statusBadge(v?: NotificationStatus): 'warning' | 'info' | 'success' | 'error' | 'default' {
  switch (v) {
    case NotificationStatus.Pending:
    case NotificationStatus.Scheduled:
    case NotificationStatus.PartiallySent: return 'warning'
    case NotificationStatus.Sending: return 'info'
    case NotificationStatus.Sent: return 'success'
    case NotificationStatus.Failed: return 'error'
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
      h(TStatusBadge, { value: row.type ?? '', type: typeBadge(row.type), label: typeLabel(row.type) }),
  },
  {
    key: 'status',
    title: 'columns.status',
    width: 100,
    render: (row) =>
      h(TStatusBadge, { value: row.status ?? '', type: statusBadge(row.status), label: statusLabel(row.status) }),
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
