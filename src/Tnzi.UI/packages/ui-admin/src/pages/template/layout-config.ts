import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TSourceBadge } from '@tnzi/ui'

/**
 * Template Layout page config — Phase 3 Task 3.37.
 *
 * Columns and form schema for the admin Template Layout management view.
 * Backend fields from LayoutInfoDto / LayoutEntityDto:
 *   id, layoutName, module, category, layoutContent, isActive, isDefault,
 *   description, metadata, creationTime, lastModificationTime
 *
 * Plan fields vs backend mapping:
 *   code    → layoutName (backend uses layoutName as unique identifier)
 *   name    → not separate; layoutName serves as both code and display name
 *   type    → BACKEND GAP: no email/pdf/html type field in LayoutInfoDto/LayoutEntityDto
 *   header  → BACKEND GAP: backend uses a single layoutContent field, no header/footer split
 *   footer  → BACKEND GAP: same as above
 *   enabled → isActive
 */

export const layoutColumns: ColumnDef[] = [
  { key: 'layoutName', title: 'columns.layoutName' },
  { key: 'module',     title: 'columns.module' },
  { key: 'category',   title: 'columns.category' },
  {
    key: 'isDefault',
    title: 'columns.isDefault',
    width: 100,
    render: (row) =>
      row.isDefault
        ? h(TStatusBadge, { value: true, type: 'info', label: 'Default' })
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  {
    key: 'isActive',
    title: 'columns.isActive',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isActive),
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
    render: (row) => h(TSourceBadge, { value: String(row.source ?? 'Database') }),
  },
]

export const layoutFormSchema: FormSchemaItem[] = [
  { key: 'layoutName',    labelKey: 'form.layoutName', label: 'Layout Name',  type: 'text',     required: true },
  { key: 'module',        labelKey: 'form.module', label: 'Module',       type: 'text',     required: true },
  { key: 'category',      labelKey: 'form.category', label: 'Category',     type: 'text' },
  { key: 'layoutContent', labelKey: 'form.layoutContent', label: 'Content',      type: 'textarea', required: true },
  { key: 'description',   labelKey: 'form.description', label: 'Description',  type: 'textarea' },
  { key: 'isDefault',     labelKey: 'form.isDefault', label: 'Default',      type: 'switch' },
  { key: 'isActive',      labelKey: 'form.isActive', label: 'Enabled',      type: 'switch' },
]
