import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

export interface RoleRow {
  id?: string
  name?: string
  code?: string
  description?: string
  enabled?: boolean
  isDefault?: boolean
  isSystem?: boolean
  creationTime?: string
}

export const roleColumns: ColumnDef<RoleRow>[] = [
  { key: 'name', title: 'columns.name', width: 200, fixed: 'left' },
  { key: 'code', title: 'columns.code', width: 160 },
  { key: 'description', title: 'columns.description' },
  {
    key: 'enabled',
    title: 'columns.enabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.enabled ?? false,
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
  {
    key: 'isSystem',
    title: 'columns.isSystem',
    width: 110,
    render: (row) =>
      row.isSystem
        ? h(TStatusBadge, { value: 'system', type: 'info', label: 'System' })
        : h(TStatusBadge, { value: 'custom', type: 'default', label: 'Custom' }),
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
  { key: 'name',        label: 'Role Name',   type: 'text',     required: true },
  { key: 'code',        label: 'Code',        type: 'text',     required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'enabled',     label: 'Enabled',     type: 'switch' },
  { key: 'isDefault',   label: 'Default Role', type: 'switch' },
]
