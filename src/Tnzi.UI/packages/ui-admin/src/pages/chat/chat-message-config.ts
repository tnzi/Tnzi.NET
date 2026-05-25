import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Aligned with backend MessageListItemDto (Tnzi.Chat).
 *
 *   id / title / messageType (1=Public / 2=Private) / senderId / senderName /
 *   canReply / creationTime / isRead / replyCount / isImportant
 *
 * Earlier columns assumed an IM session model (sessionId / content / sentAt /
 * messageType: text|image|file|system) — Tnzi.Chat is the in-app message
 * + announcement system, not IM. List was blank.
 */
interface ChatMessageRow {
  id?: string
  title?: string
  messageType?: number  // 1=Public, 2=Private
  senderId?: string
  senderName?: string
  isRead?: boolean
  isImportant?: boolean
  replyCount?: number
  creationTime?: string
}

function messageTypeLabel(v?: number): string {
  switch (v) {
    case 1: return 'Public'
    case 2: return 'Private'
    default: return '—'
  }
}

export const chatMessageColumns: ColumnDef<ChatMessageRow>[] = [
  { key: 'title', title: 'columns.title', width: 280, fixed: 'left', ellipsis: { tooltip: true } },
  {
    key: 'messageType',
    title: 'columns.messageType',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.messageType ?? 0,
        type: row.messageType === 1 ? 'info' : 'success',
        label: messageTypeLabel(row.messageType),
      }),
  },
  { key: 'senderName', title: 'columns.senderName', width: 140 },
  {
    key: 'isImportant',
    title: 'columns.isImportant',
    width: 100,
    render: (row) =>
      row.isImportant
        ? h(TStatusBadge, { value: true, type: 'warning', label: '★' })
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  {
    key: 'isRead',
    title: 'columns.isRead',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isRead ?? false,
        mapping: {
          true: { type: 'default', labelKey: 'admin.modules.chat.messages.status.read' },
          false: { type: 'info', labelKey: 'admin.modules.chat.messages.status.unread' },
        },
      }),
  },
  { key: 'replyCount', title: 'columns.replyCount', width: 90 },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

/**
 * Backend AdminMessageQueryDto accepts: messageType / isSent / senderId /
 * keyword / startDate / endDate. The page commits these into state.filters
 * via the standard CrudPage search panel.
 */
export const chatMessageSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'form.keyword', label: 'Keyword', type: 'text', placeholder: 'columns.title' },
  { key: 'senderId', labelKey: 'form.senderId', label: 'columns.senderName', type: 'text', placeholder: 'form.senderId' },
  {
    key: 'messageType',
    labelKey: 'form.messageType', label: 'columns.messageType',
    type: 'select',
    options: [
      { label: 'Public', value: 1 },
      { label: 'Private', value: 2 },
    ],
  },
]

export const chatMessageFormSchema: FormSchemaItem[] = [
  { key: 'title', labelKey: 'form.title', label: 'Title', type: 'text' },
  { key: 'senderName', labelKey: 'form.senderName', label: 'Sender', type: 'text' },
  { key: 'content', labelKey: 'form.content', label: 'Content', type: 'textarea' },
  {
    key: 'messageType',
    labelKey: 'form.messageType', label: 'Type',
    type: 'select',
    options: [
      { label: 'Public', value: 1 },
      { label: 'Private', value: 2 },
    ],
  },
  { key: 'isImportant', labelKey: 'form.isImportant', label: 'Important', type: 'switch' },
]
