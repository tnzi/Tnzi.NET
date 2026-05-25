import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Role search fields — align with backend `RoleListQueryDto`:
 * `keyword` (free text on name/description) + `isSystem` + `isDefault`.
 */
export const roleSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'form.keyword', label: 'Keyword', type: 'text', placeholder: 'columns.name' },
  {
    key: 'isDefault',
    labelKey: 'form.isDefault', label: 'Default',
    type: 'select',
    options: [
      { label: 'Default', value: 'true' },
      { label: 'Custom', value: 'false' },
    ],
  },
  {
    key: 'isSystem',
    labelKey: 'form.isSystem', label: 'System',
    type: 'select',
    options: [
      { label: 'System', value: 'true' },
      { label: 'User', value: 'false' },
    ],
  },
]

export interface RoleRow {
  id?: string
  name?: string
  normalizedName?: string
  description?: string
  isDefault?: boolean
  isSystem?: boolean
  creationTime?: string
}

export const roleColumns: ColumnDef<RoleRow>[] = [
  { key: 'name', title: 'columns.name', width: 200, fixed: 'left' },
  { key: 'description', title: 'columns.description' },
  {
    key: 'isDefault',
    title: 'columns.isDefault',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isDefault ?? false,
        mapping: {
          true: { type: 'info', labelKey: 'admin.shared.status.default' },
          false: { type: 'default', labelKey: 'admin.shared.status.custom' },
        },
      }),
  },
  {
    key: 'isSystem',
    title: 'columns.isSystem',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isSystem ?? false,
        mapping: {
          true: { type: 'warning', labelKey: 'admin.shared.status.system' },
          false: { type: 'default', labelKey: 'admin.shared.status.user' },
        },
      }),
  },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

export const roleFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Role Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'isDefault', labelKey: 'form.isDefault', label: 'Default Role', type: 'switch' },
]
