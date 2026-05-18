import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

export interface FunctionModuleRow {
  id?: string
  code?: string
  name?: string
  parentId?: string
  parentName?: string
  order?: number
  isEnabled?: boolean
}

export const functionModuleColumns: ColumnDef<FunctionModuleRow>[] = [
  { key: 'name', title: 'columns.name', width: 220, fixed: 'left' },
  { key: 'code', title: 'columns.code', width: 200 },
  { key: 'parentName', title: 'columns.parentName', width: 180 },
  { key: 'order', title: 'columns.order', width: 80 },
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

export const functionModuleFormSchema: FormSchemaItem[] = [
  { key: 'code', label: 'Module Code', type: 'text', required: true },
  { key: 'name', label: 'Display Name', type: 'text', required: true },
  // TODO(Phase H pt 3): TFunctionModuleSelector (tree)
  { key: 'parentId', label: 'Parent Module ID (leave empty for top-level)', type: 'text' },
  { key: 'order', label: 'Sort Order', type: 'number', min: 0 },
  { key: 'isEnabled', label: 'Enabled', type: 'switch' },
]
