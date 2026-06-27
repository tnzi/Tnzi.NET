import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'
import { ConversationType, type AdminConversationListItemDto } from '@tnzi/core/services/chat'

const TYPE_NS = 'admin.modules.chat.conversations.type'

/** Lowercase i18n segment for a conversation type enum value. */
export function typeKey(type: ConversationType): string {
  return type === ConversationType.Direct ? 'direct'
    : type === ConversationType.Group ? 'group'
      : 'system'
}

/** Advanced-search fields — align with backend `AdminConversationQueryDto`. */
export const conversationSearchFields: FormSchemaItem[] = [
  {
    key: 'type',
    labelKey: 'form.type',
    label: 'Type',
    type: 'select',
    options: [
      { label: 'Direct', value: ConversationType.Direct },
      { label: 'Group', value: ConversationType.Group },
      { label: 'System', value: ConversationType.System },
    ],
  },
  { key: 'userId', labelKey: 'form.userId', label: 'User ID', type: 'text', placeholder: 'form.userId' },
]

function typeBadge(type: ConversationType) {
  return h(TStatusBadge, {
    value: String(type),
    mapping: {
      [String(ConversationType.Direct)]: { type: 'info', labelKey: `${TYPE_NS}.direct` },
      [String(ConversationType.Group)]: { type: 'success', labelKey: `${TYPE_NS}.group` },
      [String(ConversationType.System)]: { type: 'warning', labelKey: `${TYPE_NS}.system` },
    },
  })
}

const columns: ColumnDef<AdminConversationListItemDto>[] = [
  { key: 'type', title: 'columns.type', width: 110, render: (r) => typeBadge(r.type) },
  { key: 'title', title: 'columns.title', minWidth: 150, render: (r) => r.title || '-' },
  { key: 'ownerName', title: 'columns.owner', minWidth: 130, render: (r) => r.ownerName || '-' },
  { key: 'memberCount', title: 'columns.members', width: 100 },
  { key: 'lastMessagePreview', title: 'columns.lastMessage', minWidth: 180, render: (r) => r.lastMessagePreview || '-' },
  {
    key: 'lastMessageAt',
    title: 'columns.lastMessageAt',
    width: 160,
    render: (r) => h(TRelativeTime, { value: r.lastMessageAt }),
  },
]

// Authored against the concrete DTO for render-time type safety, then widened
// to the prop's `ColumnDef[]` (Record<string, unknown>) shape — the render
// closures keep their typed row internally.
export const conversationColumns = columns as unknown as ColumnDef[]
