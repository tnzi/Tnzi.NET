import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { Icon } from '@iconify/vue'

interface MenuRow {
  id?: string
  name?: string
  displayName?: string
  path?: string
  component?: string
  icon?: string
  sortOrder?: number
  isHidden?: boolean
  parentId?: string
  parentName?: string
}

export const menuColumns: ColumnDef<MenuRow>[] = [
  { key: 'displayName', title: 'columns.displayName', width: 200, fixed: 'left' },
  { key: 'name', title: 'columns.name', width: 180 },
  { key: 'path', title: 'columns.path', width: 200 },
  {
    key: 'icon',
    title: 'columns.icon',
    width: 90,
    render: (row) =>
      row.icon
        ? h('span', { style: 'display: inline-flex; align-items: center; gap: 4px' }, [
            h(Icon, { icon: row.icon, width: 16, height: 16 }),
          ])
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  { key: 'sortOrder', title: 'columns.sortOrder', width: 80 },
  {
    key: 'isHidden',
    title: 'columns.isHidden',
    width: 110,
    fixed: 'right',
    render: (row) =>
      h(TStatusBadge, {
        value: row.isHidden ?? false,
        mapping: {
          true: { type: 'warning', label: 'Hidden' },
          false: { type: 'success', label: 'Visible' },
        },
      }),
  },
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
