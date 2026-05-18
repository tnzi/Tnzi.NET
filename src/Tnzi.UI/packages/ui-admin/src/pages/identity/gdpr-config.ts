import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

export interface GdprRow {
  id?: string
  userId?: string
  username?: string
  requestType?: 'export' | 'deletion'
  status?: 'pending' | 'approved' | 'denied'
  requestedAt?: string
  notes?: string
}

export const gdprColumns: ColumnDef<GdprRow>[] = [
  { key: 'username', title: 'columns.username', width: 180, fixed: 'left' },
  {
    key: 'requestType',
    title: 'columns.requestType',
    width: 130,
    render: (row) =>
      h(TStatusBadge, {
        value: row.requestType ?? 'export',
        mapping: {
          export: { type: 'info', label: 'Data Export' },
          deletion: { type: 'warning', label: 'Account Deletion' },
        },
      }),
  },
  {
    key: 'status',
    title: 'columns.status',
    width: 130,
    render: (row) =>
      h(TStatusBadge, {
        value: row.status ?? 'pending',
        mapping: {
          pending: { type: 'warning', label: 'Pending' },
          approved: { type: 'success', label: 'Approved' },
          denied: { type: 'error', label: 'Denied' },
        },
      }),
  },
  { key: 'userId', title: 'columns.userId', width: 240 },
  { key: 'notes', title: 'columns.notes' },
  {
    key: 'requestedAt',
    title: 'columns.requestedAt',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.requestedAt }),
  },
]

// View-only form (GDPR requests are submitted by end-users, not admins).
export const gdprFormSchema: FormSchemaItem[] = [
  { key: 'username', label: 'Subject User', type: 'text' },
  { key: 'userId', label: 'User ID', type: 'text' },
  {
    key: 'requestType',
    label: 'Type',
    type: 'select',
    options: [
      { label: 'Data Export', value: 'export' },
      { label: 'Account Deletion', value: 'deletion' },
    ],
  },
  {
    key: 'status',
    label: 'Status',
    type: 'select',
    options: [
      { label: 'Pending', value: 'pending' },
      { label: 'Approved', value: 'approved' },
      { label: 'Denied', value: 'denied' },
    ],
  },
  { key: 'notes', label: 'Notes', type: 'textarea' },
]
