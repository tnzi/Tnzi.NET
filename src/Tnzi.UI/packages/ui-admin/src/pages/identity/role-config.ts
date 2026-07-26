import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Role search fields - align with backend `RoleListQueryDto`:
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
  { key: 'name', title: 'columns.name', minWidth: 150 },
  { key: 'description', title: 'columns.description', minWidth: 180 },
  // Binary flags render positive-state-only: a grey "Custom"/"User" tag on
  // every ordinary row is visual noise - show a tag only when the flag is
  // set, an em dash otherwise.
  {
    key: 'isDefault',
    title: 'columns.isDefault',
    width: 110,
    render: (row) =>
      row.isDefault
        ? h(TStatusBadge, {
            value: true,
            mapping: { true: { type: 'success', labelKey: 'admin.shared.status.default' } },
          })
        : h('span', { class: 'text-muted' }, EMPTY_DASH),
  },
  {
    key: 'isSystem',
    title: 'columns.isSystem',
    width: 110,
    render: (row) =>
      row.isSystem
        ? h(TStatusBadge, {
            value: true,
            mapping: { true: { type: 'info', labelKey: 'admin.shared.status.system' } },
          })
        : h('span', { class: 'text-muted' }, EMPTY_DASH),
  },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    // 170 - absolute timestamps ("05/12/2026, 00:03:58") wrap onto two
    // lines at the previous 140.
    width: 170,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

export const roleFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Role Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'isDefault', labelKey: 'form.isDefault', label: 'Default Role', type: 'switch' },
]
