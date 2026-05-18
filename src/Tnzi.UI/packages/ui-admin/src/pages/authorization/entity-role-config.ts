import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

export interface EntityRoleRow {
  id?: string
  entityInfoId?: string
  entityName?: string
  roleId?: string
  roleName?: string
  operation?: 'Query' | 'Update' | 'Delete' | 'All'
  isEnabled?: boolean
  filter?: string
}

const OPERATION_TYPE: Record<string, 'info' | 'warning' | 'error' | 'success'> = {
  Query: 'info',
  Update: 'warning',
  Delete: 'error',
  All: 'success',
}

export const entityRoleColumns: ColumnDef<EntityRoleRow>[] = [
  { key: 'entityName', title: 'columns.entityName', width: 200, fixed: 'left' },
  { key: 'roleName', title: 'columns.roleName', width: 180 },
  {
    key: 'operation',
    title: 'columns.operation',
    width: 120,
    render: (row) =>
      h(TStatusBadge, {
        value: row.operation ?? 'Query',
        type: OPERATION_TYPE[row.operation ?? 'Query'] ?? 'default',
        label: row.operation ?? '—',
      }),
  },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isEnabled ?? false,
        mapping: {
          true: { type: 'success', label: 'Active' },
          false: { type: 'warning', label: 'Inactive' },
        },
      }),
  },
  { key: 'filter', title: 'columns.filter' },
]

export const entityRoleFormSchema: FormSchemaItem[] = [
  // TODO(Phase H pt 3): TEntitySelector + TRoleSelector
  { key: 'entityInfoId', label: 'Entity Info ID', type: 'text', required: true },
  { key: 'roleId', label: 'Role ID', type: 'text', required: true },
  {
    key: 'operation',
    label: 'Operation',
    type: 'select',
    required: true,
    options: [
      { label: 'Query (read-only)', value: 'Query' },
      { label: 'Update (mutate)', value: 'Update' },
      { label: 'Delete (remove)', value: 'Delete' },
      { label: 'All operations', value: 'All' },
    ],
  },
  { key: 'filter', label: 'Filter Expression (JSON)', type: 'textarea' },
  { key: 'isEnabled', label: 'Enabled', type: 'switch' },
]
