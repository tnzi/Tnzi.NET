import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

/**
 * Chat Session page config — Phase 3 Task 3.29 / 2026-04-14 unstub.
 *
 * Wired to /admin/chat-sessions (DefaultChatSessionAdminController).
 * Column keys and form schema fields match ChatSessionListItemDto /
 * CreateChatSessionDto / UpdateChatSessionDto exactly so TCrudPage can
 * bind without extra mapping. Status values are the ChatSessionStatus
 * enum (1=Active, 2=Archived) — kept as numbers so the backend can
 * deserialise without an intermediate string→enum step.
 */

import { h } from 'vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

interface ChatSessionRow {
  id?: string
  title?: string
  description?: string
  participants?: number
  messageCount?: number
  lastMessageAt?: string
  status?: 1 | 2
}

export const chatSessionColumns: ColumnDef<ChatSessionRow>[] = [
  { key: 'title', title: 'columns.title', width: 240, fixed: 'left' },
  { key: 'participants', title: 'columns.participants', width: 100 },
  { key: 'messageCount', title: 'columns.messageCount', width: 110 },
  {
    key: 'status',
    title: 'columns.status',
    width: 120,
    render: (row) =>
      h(TStatusBadge, {
        value: row.status ?? 0,
        mapping: {
          '1': { type: 'success', label: 'Active' },
          '2': { type: 'default', label: 'Archived' },
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
  { key: 'title',       label: 'Title',       type: 'text',     required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'status',      label: 'Status',      type: 'select',   options: [
    { label: 'Active',   value: 1 },
    { label: 'Archived', value: 2 },
  ] },
]
