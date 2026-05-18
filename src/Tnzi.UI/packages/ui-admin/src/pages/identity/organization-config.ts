import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

export interface OrganizationRow {
  id?: string
  name?: string
  code?: string
  parentId?: string
  parentName?: string
  memberCount?: number
  isEnabled?: boolean
  description?: string
  creationTime?: string
}

export const organizationColumns: ColumnDef<OrganizationRow>[] = [
  { key: 'name', title: 'columns.name', width: 220, fixed: 'left' },
  { key: 'code', title: 'columns.code', width: 160 },
  { key: 'parentName', title: 'columns.parentName', width: 160 },
  { key: 'memberCount', title: 'columns.memberCount', width: 100 },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isEnabled ?? false,
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
  { key: 'description', title: 'columns.description' },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

export const organizationFormSchema: FormSchemaItem[] = [
  { key: 'name', label: 'Organization Name', type: 'text', required: true },
  { key: 'code', label: 'Code', type: 'text', required: true },
  // TODO(Phase H pt 3): replace with TOrgSelector (single-select tree) once shipped
  { key: 'parentId', label: 'Parent ID (leave empty for root)', type: 'text' },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'isEnabled', label: 'Enabled', type: 'switch' },
]
