import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

export const featureColumns: ColumnDef[] = [
  { key: 'name',        title: 'columns.name' },
  { key: 'code',        title: 'columns.code' },
  { key: 'description', title: 'columns.description' },
  { key: 'defaultValue', title: 'columns.defaultValue' },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isEnabled),
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
]

export const featureFormSchema: FormSchemaItem[] = [
  { key: 'name',        label: 'Name',        type: 'text' },
  { key: 'code',        label: 'Code',        type: 'text' },
  { key: 'description', label: 'Description', type: 'textarea' },
  {
    key: 'valueType',
    label: 'Value Type',
    type: 'select',
    options: [
      { label: 'Boolean', value: 'boolean' },
      { label: 'String', value: 'string' },
      { label: 'Number', value: 'number' },
      { label: 'JSON', value: 'json' },
    ],
  },
  { key: 'defaultValue', label: 'Default Value', type: 'text' },
  { key: 'isEnabled',    label: 'Enabled',       type: 'switch' },
]
