import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const menuColumns: ColumnDef[] = [
  { key: 'name',        title: 'Name' },
  { key: 'path',        title: 'Path' },
  { key: 'icon',        title: 'Icon' },
  { key: 'sortOrder',   title: 'Sort' },
  { key: 'isHidden',    title: 'Hidden' },
  { key: 'parentId',    title: 'Parent', visible: false },
]

export const menuFormSchema: FormSchemaItem[] = [
  { key: 'name',        label: 'Name',        type: 'text',   required: true },
  { key: 'displayName', label: 'Display Name', type: 'text' },
  { key: 'path',        label: 'Path',        type: 'text' },
  { key: 'component',   label: 'Component',   type: 'text' },
  { key: 'icon',        label: 'Icon',        type: 'text' },
  { key: 'sortOrder',   label: 'Sort',        type: 'number', min: 0 },
  { key: 'isHidden',    label: 'Hidden',      type: 'switch' },
  { key: 'parentId',    label: 'Parent ID',   type: 'text' },
]
