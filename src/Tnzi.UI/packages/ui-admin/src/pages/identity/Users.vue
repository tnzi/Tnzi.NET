<template>
  <TCrudPage
    :state="crud"
    :all-columns="userColumns"
    :search-fields="userSearchFields"
    :title="title"
    :translate="t"
    :row-key="rowKey"
    :row-actions="rowActions"
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

  <!--
    Reset-password overlay — a custom secondary surface (distinct from the CRUD
    add/edit/view modal). Driven by `useDetail` so it is deep-linkable
    (`#reset-pwd:edit:<id>`), refresh-survivable and Back-closeable for free, and
    rendered by the single `TDetailHost` renderer like every other detail.
  -->
  <TDetailHost :state="resetPwdDetail" :title="t('actions.resetPassword')" :width="480" :translate="t">
    <template #default>
      <NForm>
        <NFormItem :label="t('actions.newPassword')" required>
          <NInput
            v-model:value="resetPwdValue"
            type="password"
            show-password-on="click"
            :placeholder="t('actions.resetPasswordHint')"
          />
        </NFormItem>
      </NForm>
    </template>
    <template #footer="{ close }">
      <NButton @click="close">{{ t('admin.crud.cancel') }}</NButton>
      <NButton type="primary" :loading="resetPwdSaving" :disabled="!resetPwdValue" @click="submitResetPassword">
        {{ t('admin.crud.confirm') }}
      </NButton>
    </template>
  </TDetailHost>

  <!--
    Manage-roles overlay (Users → More → Manage Roles). Pre-checks the roles
    already on the user (matched by name from `UserListItemDto.roles[]`, which
    carries role NAMES not IDs — the overlay owns the name→id resolution against
    the all-roles list). Submission diffs against the original set and calls
    assign/remove in parallel via `bridge.users.setRoles`. Deep-linked as
    `#roles:edit:<id>`; cold-load hydration resolves the user from the loaded
    list (`crud.items`).
  -->
  <TDetailHost :state="rolesDetail" :title="rolesTitle" :width="560" :translate="t">
    <template #default>
      <NSpin :show="rolesLoading">
        <p class="t-users-page__hint">{{ t('actions.manageRolesHint') }}</p>
        <div v-if="!rolesLoading && !availableRoles.length" class="t-users-page__empty">
          {{ t('actions.noRolesAvailable') }}
        </div>
        <NCheckboxGroup v-else v-model:value="selectedRoleIds" class="t-users-page__role-group">
          <NCheckbox
            v-for="role in availableRoles"
            :key="role.id"
            :value="role.id"
            :label="role.name"
          />
        </NCheckboxGroup>
      </NSpin>
    </template>
    <template #footer="{ close }">
      <NButton @click="close">{{ t('admin.crud.cancel') }}</NButton>
      <NButton
        type="primary"
        :loading="rolesSaving"
        :disabled="rolesLoading || !rolesUser"
        @click="submitRoles"
      >
        {{ t('admin.crud.confirm') }}
      </NButton>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import TFormSchemaRenderer from '../_shared/form-schema'
import { userColumns, userSearchFields, userFormSchema } from './user-config'
import { computed, ref, shallowRef, watch } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpin, NCheckbox, NCheckboxGroup } from 'naive-ui'
import { useSafeMessage } from '../_shared/safeMessage'
import type { RoleDto } from '@tnzi/core/services/identity'

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

const crud = useCrudPage<UserListItem>({
  pageId: 'identity.users',
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

// Custom secondary overlays resolve deep-linked users straight from the
// surrounding CRUD list via `source: crud` — a `?roles=edit:<id>` deep link
// hydrates from the loaded page, waiting for the first fetch automatically.

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

// ─── Reset-password overlay ───────────────────────────────────────────────
const resetPwdDetail = useDetail<UserListItem>({
  mode: 'modal',
  url: 'reset-pwd',
  source: crud,
})
const resetPwdValue = ref('')
const resetPwdSaving = ref(false)
// Reset the field whenever the overlay (re)binds to a user — covers in-session
// open AND a deep-link / refresh that reopens it.
watch(() => resetPwdDetail.data.value, (user) => {
  if (user) resetPwdValue.value = ''
})

async function submitResetPassword(): Promise<void> {
  const user = resetPwdDetail.data.value
  if (!user || !resetPwdValue.value) return
  resetPwdSaving.value = true
  try {
    await bridge.users.resetPassword(user.id, resetPwdValue.value)
    message.success(t('actions.resetPasswordSuccess'))
    resetPwdDetail.close()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    resetPwdSaving.value = false
  }
}

// ─── Manage-roles overlay ─────────────────────────────────────────────────
const rolesDetail = useDetail<UserListItem>({
  mode: 'modal',
  url: 'roles',
  source: crud,
})
const rolesUser = computed(() => rolesDetail.data.value)
const rolesTitle = computed(() =>
  t('actions.manageRolesTitle', { user: rolesUser.value?.userName || '—' }),
)

// `availableRoles` is the full role catalogue, loaded the first time a role
// overlay opens and reused thereafter (roles are global + change rarely).
const availableRoles = shallowRef<RoleDto[]>([])
const rolesLoaded = ref(false)
const rolesLoading = ref(false)
const rolesSaving = ref(false)
const selectedRoleIds = ref<string[]>([])
const originalRoleIds = ref<string[]>([])

async function ensureRolesLoaded(): Promise<void> {
  if (rolesLoaded.value) return
  availableRoles.value = await bridge.roles.getAll()
  rolesLoaded.value = true
}

// Load the catalogue + preselect the user's current roles whenever the overlay
// binds to a user (in-session open OR a `#roles:edit:<id>` deep link).
watch(() => rolesDetail.data.value, async (user) => {
  if (!user) return
  rolesLoading.value = true
  selectedRoleIds.value = []
  originalRoleIds.value = []
  try {
    await ensureRolesLoaded()
    // `user.roles` carries role NAMES (per UserListItemDto). Map them to ids via
    // the freshly-loaded role catalogue. Names are case-sensitive matched —
    // backend stores the canonical name on the user-role join.
    const userRoleNames = new Set(user.roles ?? [])
    const matched = availableRoles.value
      .filter((r) => userRoleNames.has(r.name))
      .map((r) => r.id)
    selectedRoleIds.value = matched
    originalRoleIds.value = [...matched]
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
    rolesDetail.close()
  } finally {
    rolesLoading.value = false
  }
})

async function submitRoles(): Promise<void> {
  const user = rolesDetail.data.value
  if (!user) return
  rolesSaving.value = true
  try {
    await bridge.users.setRoles(user.id, selectedRoleIds.value, originalRoleIds.value)
    message.success(t('actions.manageRolesSuccess'))
    rolesDetail.close()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    rolesSaving.value = false
  }
}

/**
 * Declarative operation actions. `editAction`/`deleteAction` wire to the CRUD
 * state; the per-row state toggles (enable/disable, lock/unlock) use `show`
 * predicates so only the relevant one renders. With the default
 * `maxInline=2`, this collapses to `[Edit][More▾]`. enable/disable carry a
 * `confirm` (the framework pops a dialog for More▾ entries before firing).
 *
 * Backend uses LockoutEnd as the single source of truth for both "disabled"
 * and "locked" states, so `isLockedOut` is the canonical state flag.
 */
const rowActions: RowAction<UserListItem>[] = [
  editAction(crud),
  { key: 'manageRoles', label: 'actions.manageRoles', onClick: (row) => void rolesDetail.open('edit', row) },
  { key: 'enable', label: 'actions.enable', show: (row) => row.isLockedOut === true, confirm: 'actions.confirmEnable', onClick: (row) => void handleEnable(row.id) },
  { key: 'disable', label: 'actions.disable', show: (row) => row.isLockedOut !== true, confirm: 'actions.confirmDisable', onClick: (row) => void handleDisable(row.id) },
  { key: 'unlock', label: 'actions.unlock', show: (row) => row.isLockedOut === true, onClick: (row) => void handleUnlock(row.id) },
  { key: 'lock', label: 'actions.lock', show: (row) => row.isLockedOut !== true, onClick: (row) => void handleLock(row.id) },
  { key: 'resetPassword', label: 'actions.resetPassword', onClick: (row) => void resetPwdDetail.open('edit', row) },
  deleteAction(crud),
]
</script>

<style scoped>
.t-users-page__hint {
  color: var(--tnzi-base-text-muted);
  font-size: 13px;
  line-height: 1.5;
  margin: 0 0 12px;
}
.t-users-page__empty {
  color: var(--tnzi-base-text-muted);
  font-size: 13px;
  text-align: center;
  padding: 24px 8px;
}
/* Stack the role checkboxes vertically so the modal lays out predictably
   regardless of how long the role names get. */
.t-users-page__role-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-users-page__role-group :deep(.n-checkbox) {
  margin-right: 0;
}
</style>
