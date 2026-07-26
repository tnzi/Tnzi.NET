<template>
  <TCrudPage
    :state="crud"
    :all-columns="userColumns"
    :search-fields="userSearchFields"
    :title="title"
    :translate="t"
    :row-key="rowKey"
    :row-actions="rowActions"
    :row-props="rowProps"
    :show-export="true"
    :show-import="true"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="userFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
/**
 * The user list: find an account, create one, act on one.
 *
 * Everything ABOUT a single user (profile, roles, direct grants, sessions,
 * sign-in history) lives on that user's own page, `UserDetail.vue`. This page
 * used to carry all four of those as deep-linked overlays hanging off a table
 * row, which meant investigating one account was a sequence of modals that
 * could never be seen together. A row now opens the user.
 */
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import TFormSchemaRenderer from '../_shared/form-schema'
import { userColumns, userSearchFields, userFormSchema } from './user-config'

interface UserListItem {
  id: string
  userName: string
  email?: string | null
  phoneNumber?: string | null
  nickname?: string | null
  password?: string
  creationTime?: string
  isLockedOut?: boolean
  /** Role NAMES (denormalised from UserRole.RoleName by the backend). */
  roles?: string[]
}

const title = 'title'

const bridge = createIdentityBridge({ client: useAdminClient() })
const router = useRouter()

const crud = useCrudPage<UserListItem>({
  pageId: 'identity.users',
  permission: 'user',
  columns: userColumns,
  rowKey: (u) => u.id,
  // 0.2.72+ (C4): fetch now returns the full `PagedList<T>` shape so
  // we no longer need to cast back to the 4-field tuple.
  fetchData: (query) => bridge.users.fetch(query) as never,
  createData: (data) => bridge.users.create(data as never) as Promise<UserListItem>,
  updateData: (id, data) =>
    bridge.users.update(String(id), data as never) as Promise<UserListItem>,
  deleteData: (ids) => bridge.users.delete(ids.map(String)),
  exportData: (query) => bridge.users.export!(query),
  importData: (file) => bridge.users.import!(file),
})

const rowKey = (row: unknown) => (row as UserListItem).id

const t = makePageTranslator('identity.users')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const canViewGrants = computed(() => can('authorization.userFunction.view'))

/** Open a user's page, optionally landing on one of its sections. */
function openUser(id: string, section?: string): void {
  void router.push({
    name: 'identity.users.detail',
    params: { id },
    ...(section ? { query: { section } } : {}),
  })
}

/** Whole-row click opens the account, the same as clicking its name would. */
const rowProps = (row: UserListItem) => ({
  style: 'cursor: pointer;',
  onClick: () => openUser(row.id),
})

async function withRefresh(action: () => Promise<void>, successKey: string): Promise<void> {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

const handleEnable = (id: string) => withRefresh(() => bridge.users.enable(id), 'actions.enableSuccess')
const handleDisable = (id: string) => withRefresh(() => bridge.users.disable(id), 'actions.disableSuccess')
const handleLock = (id: string) => withRefresh(() => bridge.users.lock(id), 'actions.lockSuccess')
const handleUnlock = (id: string) => withRefresh(() => bridge.users.unlock(id), 'actions.unlockSuccess')

/**
 * Row operations.
 *
 * Three rules this list follows, because a row that is itself a link makes them
 * easy to get wrong:
 *
 *  1. **No action duplicates the row click.** The whole row opens the account, so
 *     there is no "Open" button - it would be a second control doing the same
 *     thing, competing for the same pixel.
 *  2. **Every label does what it says.** "Reset Password" is NOT here: on a list
 *     row it could only navigate to the page that holds the real dialog, and a
 *     button labelled "Reset Password" that merely navigates is a lie. It lives
 *     on the account's page, next to the field it changes.
 *  3. **What stays is what can be decided while scanning**: the state toggles
 *     (enable/disable, lock/unlock) act in place; roles and permissions open the
 *     matching section of the account, and say so.
 *
 * Backend uses LockoutEnd as the single source of truth for both "disabled" and
 * "locked" states, so `isLockedOut` is the canonical state flag.
 */
const rowActions: RowAction<UserListItem>[] = [
  editAction(crud),
  { key: 'manageRoles', label: 'actions.manageRoles', show: () => crud.canUpdate, onClick: (row) => openUser(row.id, 'roles') },
  { key: 'directGrants', label: 'actions.managePermissions', show: () => canViewGrants.value, onClick: (row) => openUser(row.id, 'grants') },
  { key: 'enable', label: 'actions.enable', show: (row) => crud.canUpdate && row.isLockedOut === true, confirm: 'actions.confirmEnable', onClick: (row) => void handleEnable(row.id) },
  { key: 'disable', label: 'actions.disable', show: (row) => crud.canUpdate && row.isLockedOut !== true, confirm: 'actions.confirmDisable', onClick: (row) => void handleDisable(row.id) },
  { key: 'unlock', label: 'actions.unlock', show: (row) => crud.canUpdate && row.isLockedOut === true, onClick: (row) => void handleUnlock(row.id) },
  { key: 'lock', label: 'actions.lock', show: (row) => crud.canUpdate && row.isLockedOut !== true, onClick: (row) => void handleLock(row.id) },
  deleteAction(crud),
]
</script>
