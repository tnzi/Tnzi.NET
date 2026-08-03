import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * User search fields - align with backend `UserListQueryDto`:
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
 * Create/edit form schema. `userName` + `password` are create-only - the
 * `visible: (m) => !m.id` predicate hides them once the record has an id
 * (edit/view), mirroring the previous `v-if="mode === 'create'"`. `password`
 * uses the admin form-schema's built-in masked `password` field renderer.
 * Role assignment is NOT here - it lives on the user's own page beside their
 * direct grants, where the roles can be shown with their descriptions.
 */
export const userFormSchema: FormSchemaItem[] = [
  { key: 'userName', labelKey: 'form.userName', label: 'User Name', type: 'text', required: true, visible: (m) => !m.id },
  { key: 'password', labelKey: 'form.password', label: 'Password', type: 'password', required: true, visible: (m) => !m.id },
  { key: 'email', labelKey: 'form.email', label: 'Email', type: 'text' },
  { key: 'phoneNumber', labelKey: 'form.phoneNumber', label: 'Phone Number', type: 'text' },
  { key: 'nickname', labelKey: 'form.nickname', label: 'Nickname', type: 'text' },
]

/**
 * The FULL profile, edited on the user's own page (`UserDetail.vue`).
 *
 * Distinct from `userFormSchema` above, which stays deliberately short: the
 * list's create modal asks only for what it takes to make an account exist.
 * Everything else about a person (their name, how to reach them, who they are)
 * belongs on their page, where there is room to group it.
 */
export const userProfileSections: FormSchemaSection[] = [
  { key: 'basics', labelKey: 'admin.shared.formSections.basics', label: 'Basics', icon: 'mdi:account-outline' },
  { key: 'contact', labelKey: 'admin.shared.formSections.contact', label: 'Contact', icon: 'mdi:card-account-mail-outline' },
  { key: 'about', labelKey: 'admin.shared.formSections.notes', label: 'About', icon: 'mdi:text-account' },
]

export const userProfileSchema: FormSchemaItem[] = [
  { key: 'firstName', labelKey: 'form.firstName', label: 'First Name', type: 'text', section: 'basics' },
  { key: 'lastName', labelKey: 'form.lastName', label: 'Last Name', type: 'text', section: 'basics' },
  { key: 'nickname', labelKey: 'form.nickname', label: 'Nickname', type: 'text', section: 'basics' },
  {
    key: 'gender',
    labelKey: 'form.gender', label: 'Gender',
    type: 'select',
    section: 'basics',
    // Backend `UserDetail.Gender` is a small int; 0 = unspecified.
    options: [
      { labelKey: 'form.genderUnknown', label: 'Unspecified', value: 0 },
      { labelKey: 'form.genderMale', label: 'Male', value: 1 },
      { labelKey: 'form.genderFemale', label: 'Female', value: 2 },
    ],
  },
  { key: 'birthday', labelKey: 'form.birthday', label: 'Birthday', type: 'date', section: 'basics' },
  { key: 'email', labelKey: 'form.email', label: 'Email', type: 'text', section: 'contact' },
  { key: 'phoneNumber', labelKey: 'form.phoneNumber', label: 'Phone Number', type: 'text', section: 'contact' },
  { key: 'website', labelKey: 'form.website', label: 'Website', type: 'text', section: 'contact' },
  { key: 'address', labelKey: 'form.address', label: 'Address', type: 'text', span: 'full', section: 'contact' },
  { key: 'bio', labelKey: 'form.bio', label: 'Bio', type: 'textarea', section: 'about' },
]

// `sortable` is set on exactly the three fields `UserService.GetPagedListAsync`
// compares (`username` / `email` / `creationtime`; comparison is
// case-insensitive, so these camelCase keys match). Everything else drops to
// its `else` branch and re-sorts by CreationTime desc, which would look like
// the header did something arbitrary.
export const userColumns: ColumnDef<UserRow>[] = [
  { key: 'userName', title: 'columns.userName', minWidth: 130, sortable: true },
  { key: 'email', title: 'columns.email', minWidth: 180, sortable: true },
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
    sortable: true,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]
