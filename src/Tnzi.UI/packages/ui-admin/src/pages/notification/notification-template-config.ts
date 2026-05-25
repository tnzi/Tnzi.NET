import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TSourceBadge } from '@tnzi/ui'

/**
 * Aligned with backend TemplateInfoDto (Tnzi.Template).
 * The notification admin endpoint (/admin/notification-templates) pins
 * Module="Notification" server-side, so we don't show the module column,
 * but `category` distinguishes channels (Email / Sms / etc.).
 *
 * Real fields: templateName, module, category, defaultLayoutName,
 *              isActive, description, source ("Database"|"FileSystem"),
 *              isReadOnly, filePath, subjectTemplate, contentTemplate.
 *
 * Earlier columns used speculative names (code / name / channel / locale /
 * subject / enabled) that don't exist on the backend DTO — list was blank.
 */
interface NotificationTemplateRow {
  id?: string
  templateName?: string
  module?: string
  category?: string
  defaultLayoutName?: string
  isActive?: boolean
  description?: string
  source?: string
  isReadOnly?: boolean
  subjectTemplate?: string
  contentTemplate?: string
}

export const notificationTemplateColumns: ColumnDef<NotificationTemplateRow>[] = [
  { key: 'templateName', title: 'columns.templateName', width: 240, fixed: 'left' },
  { key: 'category', title: 'columns.category', width: 140 },
  { key: 'description', title: 'columns.description', ellipsis: { tooltip: true } },
  { key: 'defaultLayoutName', title: 'columns.defaultLayoutName', width: 160 },
  {
    key: 'isActive',
    title: 'columns.isActive',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isActive ?? false,
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  {
    key: 'source',
    title: 'columns.source',
    width: 110,
    fixed: 'right',
    render: (row) => h(TSourceBadge, { value: String(row.source ?? 'Database') }),
  },
]

export const notificationTemplateFormSchema: FormSchemaItem[] = [
  { key: 'templateName', labelKey: 'form.templateName', label: 'Template Name', type: 'text', required: true },
  { key: 'category', labelKey: 'form.category', label: 'Category', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'defaultLayoutName', labelKey: 'form.defaultLayoutName', label: 'Default Layout', type: 'text' },
  { key: 'subjectTemplate', labelKey: 'form.subjectTemplate', label: 'Subject Template', type: 'text' },
  { key: 'contentTemplate', labelKey: 'form.contentTemplate', label: 'Content Template', type: 'textarea', required: true },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch' },
]
