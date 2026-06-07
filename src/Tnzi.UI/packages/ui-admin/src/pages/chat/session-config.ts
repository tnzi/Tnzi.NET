import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Aligned with backend ChatSessionListItemDto (Tnzi.Chat).
 *   id / title / status (1=Active / 2=Archived) / participants (string[]) /
 *   messageCount / lastMessageAt / creationTime
 *
 * `participants` is a string[] of user IDs, not a count — the previous
 * column rendered it as a number and showed `[]` / a JSON dump. We render
 * `.length` so the column reads as a participant count.
 */
interface ChatSessionRow {
  id?: string
  title?: string
  description?: string
  participants?: string[]
  messageCount?: number
  lastMessageAt?: string
  status?: 1 | 2
}

export const chatSessionColumns: ColumnDef<ChatSessionRow>[] = [
  { key: 'title', title: 'columns.title', width: 240, fixed: 'left' },
  {
    key: 'participants',
    title: 'columns.participants',
    width: 110,
    render: (row) => String(row.participants?.length ?? 0),
  },
  { key: 'messageCount', title: 'columns.messageCount', width: 110 },
  {
    key: 'status',
    title: 'columns.status',
    width: 120,
    render: (row) =>
      h(TStatusBadge, {
        value: row.status ?? 0,
        mapping: {
          '1': { type: 'success', labelKey: 'admin.shared.status.active' },
          '2': { type: 'default', labelKey: 'admin.modules.chat.sessions.status.archived' },
        },
      }),
  },
  {
    key: 'lastMessageAt',
    title: 'columns.lastMessageAt',
    width: 160,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.lastMessageAt }),
  },
]

export const chatSessionFormSchema: FormSchemaItem[] = [
  { key: 'title', labelKey: 'form.title', label: 'Title', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'status',
    labelKey: 'form.status', label: 'Status',
    type: 'select',
    options: [
      { label: 'Active', value: 1 },
      { label: 'Archived', value: 2 },
    ],
  },
]
