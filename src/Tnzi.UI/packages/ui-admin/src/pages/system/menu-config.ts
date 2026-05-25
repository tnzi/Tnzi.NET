/**
 * Menu config — column shape + form schema for `system.menus`.
 *
 * Columns and form schema are aligned with backend `MenuDto`:
 *   id / parentId / name / icon / path / component / sortOrder /
 *   isHidden / permission / type / creationTime / lastModificationTime
 *
 * Note the backend has NO `displayName` — that field only exists on the
 * generated `MenuInfoDto` (ABP-style padding); JSON deserialised from the
 * `/admin/menus` response leaves it undefined. The page renders `name` as
 * the primary label instead.
 *
 * MenuType enum: 0=Directory, 1=Menu, 2=Button.
 */
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { Icon } from '@iconify/vue'

export interface MenuRow {
  id?: string
  parentId?: string | null
  name?: string
  path?: string
  component?: string
  icon?: string
  sortOrder?: number
  isHidden?: boolean
  permission?: string
  type?: number
}

function menuTypeLabel(v?: number): string {
  switch (v) {
    case 0: return 'Directory'
    case 1: return 'Menu'
    case 2: return 'Button'
    default: return '—'
  }
}

function menuTypeBadgeType(v?: number): 'info' | 'success' | 'warning' | 'default' {
  switch (v) {
    case 0: return 'info'
    case 1: return 'success'
    case 2: return 'warning'
    default: return 'default'
  }
}

export const menuColumns: ColumnDef<MenuRow>[] = [
  { key: 'name', title: 'columns.name', width: 220, fixed: 'left' },
  {
    key: 'icon',
    title: 'columns.icon',
    width: 80,
    render: (row) =>
      row.icon
        ? h('span', { style: 'display: inline-flex; align-items: center; gap: 4px' }, [
            h(Icon, { icon: row.icon, width: 16, height: 16 }),
          ])
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  { key: 'path', title: 'columns.path', width: 220 },
  { key: 'permission', title: 'columns.permission', width: 180 },
  {
    key: 'type',
    title: 'columns.type',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.type ?? 0,
        type: menuTypeBadgeType(row.type),
        label: menuTypeLabel(row.type),
      }),
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
          true: { type: 'warning', labelKey: 'admin.modules.system.menus.status.hidden' },
          false: { type: 'success', labelKey: 'admin.modules.system.menus.status.visible' },
        },
      }),
  },
]

export const menuFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'type',
    labelKey: 'form.type', label: 'Type',
    type: 'select',
    required: true,
    // MenuType enum: 0=Directory, 1=Menu, 2=Button
    options: [
      { label: 'Directory', value: 0 },
      { label: 'Menu', value: 1 },
      { label: 'Button', value: 2 },
    ],
  },
  { key: 'parentId', labelKey: 'form.parentId', label: 'Parent', type: 'text' },
  { key: 'path', labelKey: 'form.path', label: 'Path', type: 'text' },
  { key: 'component', labelKey: 'form.component', label: 'Component', type: 'text' },
  { key: 'icon', labelKey: 'form.icon', label: 'Icon', type: 'text' },
  { key: 'permission', labelKey: 'form.permission', label: 'Permission Code', type: 'text' },
  { key: 'sortOrder', labelKey: 'form.sortOrder', label: 'Sort', type: 'number', min: 0 },
  { key: 'isHidden', labelKey: 'form.isHidden', label: 'Hidden', type: 'switch' },
]
