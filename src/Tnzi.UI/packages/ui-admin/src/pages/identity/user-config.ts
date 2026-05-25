import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * User search fields — align with backend `UserListQueryDto`:
 * `keyword` (free text on userName/email/phoneNumber) + structured filters
 * `isLockedOut` / `isEmailConfirmed`.
 * Earlier per-field free-text inputs (userName/displayName/email/phone)
 * never matched backend bindings and were silently dropped.
 */
export const userSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'form.keyword', label: 'Keyword', type: 'text', placeholder: 'form.keywordHint' },
  {
    key: 'isLockedOut',
    labelKey: 'form.isLockedOut', label: 'Lock Status',
    type: 'select',
    options: [
      { label: 'Locked', value: 'true' },
      { label: 'Unlocked', value: 'false' },
    ],
  },
  {
    key: 'isEmailConfirmed',
    labelKey: 'form.isEmailConfirmed', label: 'Email Confirmed',
    type: 'select',
    options: [
      { label: 'Confirmed', value: 'true' },
      { label: 'Unconfirmed', value: 'false' },
    ],
  },
]

export interface UserRow {
  id?: string
  userName?: string
  email?: string
  phoneNumber?: string
  organizationName?: string
  isLockedOut?: boolean
  isEmailConfirmed?: boolean
  twoFactorEnabled?: boolean
  roles?: string[]
  creationTime?: string
}

export const userColumns: ColumnDef<UserRow>[] = [
  { key: 'userName', title: 'columns.userName', width: 160, fixed: 'left' },
  { key: 'email', title: 'columns.email', width: 220 },
  { key: 'phoneNumber', title: 'columns.phoneNumber', width: 140 },
  { key: 'organizationName', title: 'columns.organizationName', width: 160 },
  {
    key: 'isLockedOut',
    title: 'columns.isLockedOut',
    width: 100,
    render: (row) =>
      row.isLockedOut
        ? h(TStatusBadge, { value: true, type: 'error', labelKey: 'admin.shared.status.locked' })
        : h(TStatusBadge, { value: false, type: 'success', labelKey: 'admin.shared.status.active' }),
  },
  {
    key: 'isEmailConfirmed',
    title: 'columns.isEmailConfirmed',
    width: 130,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isEmailConfirmed ?? false,
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.confirmed' },
          false: { type: 'warning', labelKey: 'admin.shared.status.unconfirmed' },
        },
      }),
  },
  {
    key: 'twoFactorEnabled',
    title: 'columns.twoFactorEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.twoFactorEnabled ?? false,
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'default', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]
