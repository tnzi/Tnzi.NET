import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * Template page config — Phase 3 Task 3.36.
 *
 * Columns and form schema for the admin Template management view.
 * Backend fields from TemplateInfoDto / TemplateEntityDto:
 *   id, templateName, module, category, defaultLayoutName, isActive,
 *   description, subjectTemplate, contentTemplate, metadata,
 *   creationTime, lastModificationTime
 *
 * Plan fields vs backend mapping:
 *   code      → templateName (backend uses templateName as unique identifier)
 *   name      → not separate; templateName serves as both code and display name
 *   category  → category ✓
 *   layoutId  → defaultLayoutName (backend references layout by name, not ID)
 *   version   → BACKEND GAP: no version field in TemplateInfoDto/TemplateEntityDto
 *   enabled   → isActive
 */

export const templateColumns: ColumnDef[] = [
  { key: 'templateName',      title: 'columns.templateName' },
  { key: 'module',            title: 'columns.module' },
  { key: 'category',          title: 'columns.category' },
  { key: 'defaultLayoutName', title: 'columns.defaultLayoutName' },
  {
    key: 'isActive',
    title: 'columns.isActive',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isActive),
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
]

export const templateFormSchema: FormSchemaItem[] = [
  { key: 'templateName',      label: 'Template Name',    type: 'text',     required: true },
  { key: 'module',            label: 'Module',           type: 'text',     required: true },
  { key: 'category',          label: 'Category',         type: 'text' },
  { key: 'defaultLayoutName', label: 'Layout Name',      type: 'text' },
  { key: 'subjectTemplate',   label: 'Subject',          type: 'text' },
  { key: 'contentTemplate',   label: 'Content',          type: 'textarea', required: true },
  { key: 'description',       label: 'Description',      type: 'textarea' },
  { key: 'isActive',          label: 'Enabled',          type: 'switch' },
]
