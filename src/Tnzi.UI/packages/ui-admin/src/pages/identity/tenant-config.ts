import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

export interface TenantRow {
  id?: string
  name?: string
  code?: string
  status?: 'active' | 'suspended' | 'inactive'
  plan?: 'free' | 'pro' | 'enterprise'
  memberCount?: number
  creationTime?: string
}

export const tenantColumns: ColumnDef<TenantRow>[] = [
  { key: 'name', title: 'columns.name', width: 200, fixed: 'left' },
  { key: 'code', title: 'columns.code', width: 160 },
  {
    key: 'plan',
    title: 'columns.plan',
    width: 120,
    render: (row) =>
      h(TStatusBadge, {
        value: row.plan ?? 'free',
        mapping: {
          free: { type: 'default', label: 'Free' },
          pro: { type: 'info', label: 'Pro' },
          enterprise: { type: 'warning', label: 'Enterprise' },
        },
      }),
  },
  {
    key: 'status',
    title: 'columns.status',
    width: 120,
    render: (row) =>
      h(TStatusBadge, {
        value: row.status ?? 'inactive',
        mapping: {
          active: { type: 'success', label: 'Active' },
          suspended: { type: 'error', label: 'Suspended' },
          inactive: { type: 'default', label: 'Inactive' },
        },
      }),
  },
  { key: 'memberCount', title: 'columns.memberCount', width: 100 },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

export const tenantFormSchema: FormSchemaItem[] = [
  { key: 'name', label: 'Tenant Name', type: 'text', required: true },
  { key: 'code', label: 'Code', type: 'text', required: true },
  {
    key: 'plan',
    label: 'Subscription Plan',
    type: 'select',
    options: [
      { label: 'Free', value: 'free' },
      { label: 'Pro', value: 'pro' },
      { label: 'Enterprise', value: 'enterprise' },
    ],
  },
  {
    key: 'status',
    label: 'Status',
    type: 'select',
    options: [
      { label: 'Active', value: 'active' },
      { label: 'Suspended', value: 'suspended' },
      { label: 'Inactive', value: 'inactive' },
    ],
  },
]
