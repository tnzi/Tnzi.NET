import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

/**
 * soybean-style search panel. Each entry maps to one cell in the
 * 4-column NGrid form. Values commit to `state.query.filters` on
 * Search click. Backend filter contract: free-text per field.
 */
export const userSearchFields: FormSchemaItem[] = [
  { key: 'userName', label: 'columns.username', type: 'text', placeholder: 'columns.username' },
  { key: 'displayName', label: 'columns.displayName', type: 'text', placeholder: 'columns.displayName' },
  { key: 'email', label: 'columns.email', type: 'text', placeholder: 'columns.email' },
  { key: 'phoneNumber', label: 'columns.phoneNumber', type: 'text', placeholder: 'columns.phoneNumber' },
  {
    key: 'isEnabled',
    label: 'columns.status',
    type: 'select',
    options: [
      { label: 'Enabled', value: 'true' },
      { label: 'Disabled', value: 'false' },
    ],
  },
]

export interface UserRow {
  id?: string
  userName?: string
  email?: string
  phoneNumber?: string
  displayName?: string
  isEnabled?: boolean
  isLockedOut?: boolean
  lastLoginTime?: string
  creationTime?: string
}

export const userColumns: ColumnDef<UserRow>[] = [
  { key: 'userName', title: 'columns.username', width: 160, fixed: 'left' },
  { key: 'displayName', title: 'columns.displayName', width: 160 },
  { key: 'email', title: 'columns.email', width: 220 },
  { key: 'phoneNumber', title: 'columns.phoneNumber', width: 140 },
  {
    key: 'isEnabled',
    title: 'columns.status',
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
  {
    key: 'isLockedOut',
    title: 'columns.lock',
    width: 80,
    render: (row) =>
      row.isLockedOut
        ? h(TStatusBadge, { value: true, type: 'warning', label: 'Locked' })
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  {
    key: 'lastLoginTime',
    title: 'columns.lastLogin',
    width: 140,
    render: (row) => h(TRelativeTime, { value: row.lastLoginTime }),
  },
  {
    key: 'creationTime',
    title: 'columns.created',
    width: 140,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]
