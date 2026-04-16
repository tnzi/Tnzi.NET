import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

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
  { key: 'layoutName', title: 'Layout Name' },
  { key: 'module',     title: 'Module' },
  { key: 'category',   title: 'Category' },
  { key: 'isDefault',  title: 'Default' },
  { key: 'isActive',   title: 'Enabled' },
]

export const layoutFormSchema: FormSchemaItem[] = [
  { key: 'layoutName',    label: 'Layout Name',  type: 'text',     required: true },
  { key: 'module',        label: 'Module',       type: 'text',     required: true },
  { key: 'category',      label: 'Category',     type: 'text' },
  { key: 'layoutContent', label: 'Content',      type: 'textarea', required: true },
  { key: 'description',   label: 'Description',  type: 'textarea' },
  { key: 'isDefault',     label: 'Default',      type: 'switch' },
  { key: 'isActive',      label: 'Enabled',      type: 'switch' },
]
