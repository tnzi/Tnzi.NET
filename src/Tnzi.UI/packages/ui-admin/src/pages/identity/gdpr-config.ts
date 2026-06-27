import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

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
  { key: 'username', title: 'columns.username', minWidth: 140 },
  {
    key: 'requestType',
    title: 'columns.requestType',
    width: 130,
    render: (row) =>
      h(TStatusBadge, {
        value: row.requestType ?? 'export',
        mapping: {
          export: { type: 'info', labelKey: 'admin.modules.identity.gdpr.requestType.export' },
          deletion: { type: 'warning', labelKey: 'admin.modules.identity.gdpr.requestType.deletion' },
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
          pending: { type: 'warning', labelKey: 'admin.shared.status.pending' },
          approved: { type: 'success', labelKey: 'admin.shared.status.approved' },
          denied: { type: 'error', labelKey: 'admin.shared.status.rejected' },
        },
      }),
  },
  { key: 'userId', title: 'columns.userId', minWidth: 180 },
  { key: 'notes', title: 'columns.notes', minWidth: 160 },
  {
    key: 'requestedAt',
    title: 'columns.requestedAt',
    width: 150,
    render: (row) => h(TRelativeTime, { value: row.requestedAt }),
  },
]

// View-only form (GDPR requests are submitted by end-users, not admins).
export const gdprFormSchema: FormSchemaItem[] = [
  { key: 'username', labelKey: 'form.username', label: 'Subject User', type: 'text' },
  { key: 'userId', labelKey: 'form.userId', label: 'User ID', type: 'text' },
  {
    key: 'requestType',
    labelKey: 'form.requestType', label: 'Type',
    type: 'select',
    options: [
      { labelKey: 'admin.modules.identity.gdpr.requestType.export', label: 'Data Export', value: 'export' },
      { labelKey: 'admin.modules.identity.gdpr.requestType.deletion', label: 'Account Deletion', value: 'deletion' },
    ],
  },
  {
    key: 'status',
    labelKey: 'form.status', label: 'Status',
    type: 'select',
    options: [
      { labelKey: 'admin.shared.status.pending', label: 'Pending', value: 'pending' },
      { labelKey: 'admin.shared.status.approved', label: 'Approved', value: 'approved' },
      { labelKey: 'admin.shared.status.rejected', label: 'Denied', value: 'denied' },
    ],
  },
  { key: 'notes', labelKey: 'form.notes', label: 'Notes', type: 'textarea' },
]
