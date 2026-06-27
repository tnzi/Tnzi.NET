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

/**
 * Create/edit form schema. `userName` + `password` are create-only — the
 * `visible: (m) => !m.id` predicate hides them once the record has an id
 * (edit/view), mirroring the previous `v-if="mode === 'create'"`. `password`
 * uses the admin form-schema's built-in masked `password` field renderer.
 * Role assignment is NOT here — it has its own diff-aware "Manage Roles" modal
 * (name→id resolution + assign/remove), which the main form can't express.
 */
export const userFormSchema: FormSchemaItem[] = [
  { key: 'userName', labelKey: 'form.userName', label: 'User Name', type: 'text', required: true, visible: (m) => !m.id },
  { key: 'password', labelKey: 'form.password', label: 'Password', type: 'password', required: true, visible: (m) => !m.id },
  { key: 'email', labelKey: 'form.email', label: 'Email', type: 'text' },
  { key: 'phoneNumber', labelKey: 'form.phoneNumber', label: 'Phone Number', type: 'text' },
  { key: 'nickname', labelKey: 'form.nickname', label: 'Nickname', type: 'text' },
]

export const userColumns: ColumnDef<UserRow>[] = [
  { key: 'userName', title: 'columns.userName', minWidth: 130 },
  { key: 'email', title: 'columns.email', minWidth: 180 },
  { key: 'phoneNumber', title: 'columns.phoneNumber', minWidth: 130 },
  { key: 'organizationName', title: 'columns.organizationName', minWidth: 140 },
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
    width: 150,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]
