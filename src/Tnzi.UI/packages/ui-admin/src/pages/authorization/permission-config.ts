import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

export interface PermissionRow {
  id?: string
  code?: string
  name?: string
  moduleId?: string
  moduleName?: string
  isEnabled?: boolean
  description?: string
}

export const permissionColumns: ColumnDef<PermissionRow>[] = [
  { key: 'name', title: 'columns.name', width: 220, fixed: 'left' },
  { key: 'code', title: 'columns.code', width: 220 },
  { key: 'moduleName', title: 'columns.moduleName', width: 180 },
  { key: 'description', title: 'columns.description' },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    fixed: 'right',
    render: (row) =>
      h(TStatusBadge, {
        value: row.isEnabled ?? false,
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
]

export const permissionFormSchema: FormSchemaItem[] = [
  { key: 'code', label: 'Permission Code', type: 'text', required: true },
  { key: 'name', label: 'Display Name', type: 'text', required: true },
  // TODO(Phase H pt 3): TFunctionModuleSelector
  { key: 'moduleId', label: 'Module ID', type: 'text', required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'isEnabled', label: 'Enabled', type: 'switch' },
]
