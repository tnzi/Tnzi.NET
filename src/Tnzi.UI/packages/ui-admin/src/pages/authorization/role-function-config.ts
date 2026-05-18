import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

/**
 * RoleFunction page — read-only listing of role↔permission assignments.
 * Mutation happens via batch endpoints (POST /admin/role-functions/role/{roleId}/assign)
 * — a dedicated "Assign Permissions" dialog is on the Phase H pt 3 roadmap.
 */
export interface RoleFunctionRow {
  id?: string
  roleId?: string
  roleName?: string
  functionId?: string
  functionCode?: string
  functionName?: string
  moduleId?: string
  moduleName?: string
  isEnabled?: boolean
  creationTime?: string
}

export const roleFunctionColumns: ColumnDef<RoleFunctionRow>[] = [
  { key: 'roleName', title: 'columns.roleName', width: 180, fixed: 'left' },
  { key: 'functionName', title: 'columns.functionName', width: 220 },
  { key: 'functionCode', title: 'columns.functionCode', width: 220 },
  { key: 'moduleName', title: 'columns.moduleName', width: 160 },
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
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

// Detail-view form for inspecting an existing assignment.
export const roleFunctionFormSchema: FormSchemaItem[] = [
  { key: 'roleName', label: 'Role', type: 'text' },
  { key: 'functionCode', label: 'Permission Code', type: 'text' },
  { key: 'functionName', label: 'Permission Name', type: 'text' },
  { key: 'moduleName', label: 'Module', type: 'text' },
]
