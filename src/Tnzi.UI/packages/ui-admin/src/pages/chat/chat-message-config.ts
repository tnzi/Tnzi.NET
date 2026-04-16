import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

/**
 * Chat Message page config — Phase 3 Task 3.30.
 *
 * Columns and form schema for the admin Chat Message view.
 * When route has ?sessionId=..., the page auto-injects a session filter.
 *
 * Backend fields from MessageListItemDto:
 *   id, title, messageType, senderId, senderName, canReply,
 *   creationTime, isRead, replyCount, isImportant
 *
 * The plan uses sessionId/senderId/content/messageType/sentAt as display keys.
 * MessageListItemDto has senderId, messageType, and creationTime (mapped to sentAt).
 * sessionId is a filter context, not a DTO field; shown as display column for context.
 */

export const chatMessageColumns: ColumnDef[] = [
  { key: 'sessionId',   title: 'Session' },
  { key: 'senderId',    title: 'Sender' },
  { key: 'content',     title: 'Content' },
  { key: 'messageType', title: 'Type' },
  { key: 'sentAt',      title: 'Sent At' },
]

export const chatMessageFormSchema: FormSchemaItem[] = [
  { key: 'sessionId',   label: 'Session',  type: 'text',     required: true },
  { key: 'senderId',    label: 'Sender',   type: 'text' },
  { key: 'content',     label: 'Content',  type: 'textarea', required: true },
  { key: 'messageType', label: 'Type',     type: 'select',   options: [
    { label: 'Text',   value: 'text' },
    { label: 'Image',  value: 'image' },
    { label: 'File',   value: 'file' },
    { label: 'System', value: 'system' },
  ] },
]
