import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Login log search fields — align with backend `LoginLogQueryDto`:
 * `userId` / `ipAddress` / `startDate` / `endDate` / `isSuccess`.
 */
export const loginLogSearchFields: FormSchemaItem[] = [
  { key: 'userId', labelKey: 'form.userId', label: 'User ID', type: 'text', placeholder: 'form.userId' },
  { key: 'ipAddress', labelKey: 'form.ipAddress', label: 'IP Address', type: 'text', placeholder: 'form.ipAddress' },
  {
    key: 'isSuccess',
    labelKey: 'form.isSuccess', label: 'Success',
    type: 'select',
    options: [
      { label: 'Success', value: 'true' },
      { label: 'Failed', value: 'false' },
    ],
  },
  { key: 'startDate', labelKey: 'form.startDate', label: 'Start Date', type: 'date' },
  { key: 'endDate', labelKey: 'form.endDate', label: 'End Date', type: 'date' },
]

interface LoginLogRow {
  id?: string
  userId?: string
  userName?: string
  ipAddress?: string
  userAgent?: string
  isSuccess?: boolean
  failureReason?: string
  loginTime?: string
}

export const loginLogColumns: ColumnDef<LoginLogRow>[] = [
  { key: 'userName', title: 'columns.userName', width: 160, fixed: 'left' },
  { key: 'ipAddress', title: 'columns.ipAddress', width: 140 },
  {
    key: 'isSuccess',
    title: 'columns.isSuccess',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isSuccess ?? false,
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.success' },
          false: { type: 'error', labelKey: 'admin.shared.status.failed' },
        },
      }),
  },
  { key: 'failureReason', title: 'columns.failureReason', width: 200 },
  { key: 'userAgent', title: 'columns.userAgent' },
  {
    key: 'loginTime',
    title: 'columns.loginTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.loginTime }),
  },
]

// View-mode form (read-only detail panel — login logs are immutable).
export const loginLogFormSchema: FormSchemaItem[] = [
  { key: 'userName', labelKey: 'form.userName', label: 'Username', type: 'text' },
  { key: 'ipAddress', labelKey: 'form.ipAddress', label: 'IP Address', type: 'text' },
  { key: 'userAgent', labelKey: 'form.userAgent', label: 'User Agent', type: 'textarea' },
  { key: 'isSuccess', labelKey: 'form.isSuccess', label: 'Success', type: 'switch' },
  { key: 'failureReason', labelKey: 'form.failureReason', label: 'Failure Reason', type: 'text' },
  { key: 'loginTime', labelKey: 'form.loginTime', label: 'Login Time', type: 'date' },
]
