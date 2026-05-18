import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

interface LoginLogRow {
  username?: string
  ip?: string
  userAgent?: string
  success?: boolean
  loginAt?: string
  location?: string
}

export const loginLogColumns: ColumnDef<LoginLogRow>[] = [
  { key: 'username', title: 'columns.username', width: 160, fixed: 'left' },
  { key: 'ip', title: 'columns.ip', width: 140 },
  {
    key: 'success',
    title: 'columns.success',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.success ?? false,
        mapping: {
          true: { type: 'success', label: 'Success' },
          false: { type: 'error', label: 'Failed' },
        },
      }),
  },
  { key: 'location', title: 'columns.location', width: 160 },
  { key: 'userAgent', title: 'columns.userAgent' },
  {
    key: 'loginAt',
    title: 'columns.loginAt',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.loginAt }),
  },
]

// View-mode form (read-only detail panel — login logs are immutable).
export const loginLogFormSchema: FormSchemaItem[] = [
  { key: 'username', label: 'Username', type: 'text' },
  { key: 'ip', label: 'IP Address', type: 'text' },
  { key: 'userAgent', label: 'User Agent', type: 'textarea' },
  { key: 'success', label: 'Success', type: 'switch' },
  { key: 'location', label: 'Location', type: 'text' },
  { key: 'loginAt', label: 'Login At', type: 'date' },
]
