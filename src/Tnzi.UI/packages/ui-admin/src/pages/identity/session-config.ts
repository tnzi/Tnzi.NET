import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

export interface SessionRow {
  id?: string
  userName?: string
  ip?: string
  userAgent?: string
  location?: string
  loginTime?: string
  lastActiveAt?: string
  isActive?: boolean
}

export const sessionColumns: ColumnDef<SessionRow>[] = [
  { key: 'userName', title: 'columns.userName', width: 160, fixed: 'left' },
  {
    key: 'isActive',
    title: 'columns.isActive',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isActive ?? false,
        mapping: {
          true: { type: 'success', label: 'Online' },
          false: { type: 'default', label: 'Offline' },
        },
      }),
  },
  { key: 'ip', title: 'columns.ip', width: 140 },
  { key: 'location', title: 'columns.location', width: 160 },
  { key: 'userAgent', title: 'columns.userAgent' },
  {
    key: 'loginTime',
    title: 'columns.loginTime',
    width: 140,
    render: (row) => h(TRelativeTime, { value: row.loginTime }),
  },
  {
    key: 'lastActiveAt',
    title: 'columns.lastActiveAt',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.lastActiveAt }),
  },
]
