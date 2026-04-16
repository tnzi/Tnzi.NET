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

export const chatSessionColumns: ColumnDef[] = [
  { key: 'title',         title: 'Title' },
  { key: 'participants',  title: 'Participants' },
  { key: 'messageCount',  title: 'Messages' },
  { key: 'lastMessageAt', title: 'Last Message' },
  { key: 'status',        title: 'Status' },
]

export const chatSessionFormSchema: FormSchemaItem[] = [
  { key: 'title',       label: 'Title',       type: 'text',     required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'status',      label: 'Status',      type: 'select',   options: [
    { label: 'Active',   value: 1 },
    { label: 'Archived', value: 2 },
  ] },
]
