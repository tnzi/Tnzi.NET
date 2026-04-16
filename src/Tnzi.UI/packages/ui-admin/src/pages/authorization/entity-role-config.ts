import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const entityRoleColumns: ColumnDef[] = [
  { key: 'entityInfoId', title: 'Entity Info' },
  { key: 'roleId',       title: 'Role' },
  { key: 'operation',    title: 'Operation' },
  { key: 'isEnabled',    title: 'Enabled' },
  { key: 'filter',       title: 'Filter', visible: false },
]

export const entityRoleFormSchema: FormSchemaItem[] = [
  { key: 'entityInfoId', label: 'Entity Info ID', type: 'text', required: true },
  { key: 'roleId',       label: 'Role ID',        type: 'text', required: true },
  { key: 'operation',    label: 'Operation',      type: 'select', required: true, options: [
    { label: 'Query',  value: 'Query' },
    { label: 'Update', value: 'Update' },
    { label: 'Delete', value: 'Delete' },
    { label: 'All',    value: 'All' },
  ] },
  { key: 'filter',       label: 'Filter (JSON)',  type: 'textarea' },
  { key: 'isEnabled',    label: 'Enabled',        type: 'switch' },
]
