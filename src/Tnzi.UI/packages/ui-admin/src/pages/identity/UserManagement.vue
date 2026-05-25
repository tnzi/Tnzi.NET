<template>
  <TCrudPage
    :state="crud"
    :all-columns="userColumns"
    :search-fields="userSearchFields"
    :title="title"
    :translate="t"
    :row-key="rowKey"
    :show-export="true"
    :show-import="true"
  >
    <template #form="{ mode }">
      <NForm :disabled="mode === 'view'">
        <NFormItem v-if="mode === 'create'" :label="t('form.userName')" path="userName" required>
          <NInput
            :value="userNameValue"
            @update:value="(v: string) => setField('userName', v)"
          />
        </NFormItem>
        <NFormItem v-if="mode === 'create'" :label="t('form.password')" path="password" required>
          <NInput
            type="password"
            show-password-on="click"
            :value="passwordValue"
            @update:value="(v: string) => setField('password', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.email')" path="email">
          <NInput
            :value="emailValue"
            @update:value="(v: string) => setField('email', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.phoneNumber')" path="phoneNumber">
          <NInput
            :value="phoneNumberValue"
            @update:value="(v: string) => setField('phoneNumber', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.nickname')" path="nickname">
          <NInput
            :value="nicknameValue"
            @update:value="(v: string) => setField('nickname', v)"
          />
        </NFormItem>
      </NForm>
    </template>
    <template #rowActions="{ row }">
      <!--
        Strict 2-button surface: [Edit] [More▾]. Manage Roles, Enable/Disable
        toggle, Lock/Unlock toggle and Reset Password all live inside More
        along with the auto-injected Delete entry. Confirmation for each
        destructive entry is delegated to `useDialog().warning(...)` via
        TRowActions (Delete) or the `withConfirm` helper below (others).
      -->
      <TRowActions
        :row="(row as UserListItem)"
        :state="crud"
        :translate="t"
        :more-options="moreOptionsFor"
        :on-more-select="onMoreSelect"
      />
    </template>
  </TCrudPage>

  <NModal v-model:show="resetPwdModal.show" :title="t('actions.resetPassword')" preset="card" style="width: 480px">
    <NForm>
      <NFormItem :label="t('actions.newPassword')" required>
        <NInput
          v-model:value="resetPwdModal.password"
          type="password"
          show-password-on="click"
          :placeholder="t('actions.resetPasswordHint')"
        />
      </NFormItem>
    </NForm>
    <template #footer>
      <div style="display: flex; justify-content: flex-end; gap: 8px">
        <NButton @click="resetPwdModal.show = false">{{ t('admin.crud.cancel') }}</NButton>
        <NButton type="primary" :disabled="!resetPwdModal.password" @click="submitResetPassword">
          {{ t('admin.crud.confirm') }}
        </NButton>
      </div>
    </template>
  </NModal>

  <!--
    Manage-roles modal — shown when the row More menu selects "Manage Roles".
    Pre-checks the roles already on the user (matched by name from
    `UserListItemDto.roles[]` since the list DTO carries role NAMES, not
    IDs — the modal owns the name→id resolution against the all-roles list).
    Submission diffs against the original set and calls assign/remove in
    parallel via `bridge.users.setRoles`.
  -->
  <NModal v-model:show="rolesModal.show" :title="rolesModalTitle" preset="card" style="width: 560px">
    <NSpin :show="rolesModal.loading">
      <p class="t-users-page__hint">{{ t('actions.manageRolesHint') }}</p>
      <div v-if="!rolesModal.loading && !availableRoles.length" class="t-users-page__empty">
        {{ t('actions.noRolesAvailable') }}
      </div>
      <NCheckboxGroup
        v-else
        v-model:value="rolesModal.selectedIds"
        class="t-users-page__role-group"
      >
        <NCheckbox
          v-for="role in availableRoles"
          :key="role.id"
          :value="role.id"
          :label="role.name"
        />
      </NCheckboxGroup>
    </NSpin>
    <template #footer>
      <div style="display: flex; justify-content: flex-end; gap: 8px">
        <NButton @click="rolesModal.show = false">{{ t('admin.crud.cancel') }}</NButton>
        <NButton
          type="primary"
          :loading="rolesModal.saving"
          :disabled="rolesModal.loading || !rolesModal.userId"
          @click="submitRoles"
        >
          {{ t('admin.crud.confirm') }}
        </NButton>
      </div>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { interpolate, translatePageKey } from '../_shared/translate'
import { userColumns, userSearchFields } from './user-config'
import { computed, reactive, ref, shallowRef } from 'vue'
import {
  NForm,
  NFormItem,
  NInput,
  NButton,
  NModal,
  NSpin,
  NCheckbox,
  NCheckboxGroup,
  useDialog,
} from 'naive-ui'
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
const userNameValue = computed(() => crud.formModal.formData.value?.userName ?? '')
const passwordValue = computed(() => crud.formModal.formData.value?.password ?? '')
const emailValue = computed(() => crud.formModal.formData.value?.email ?? '')
const phoneNumberValue = computed(() => crud.formModal.formData.value?.phoneNumber ?? '')
const nicknameValue = computed(() => crud.formModal.formData.value?.nickname ?? '')

crud.refresh().catch(() => undefined)

function setField(key: keyof UserListItem, value: unknown) {
  if (!crud.formModal.formData.value) {
    crud.formModal.formData.value = {} as UserListItem
  }
  ;(crud.formModal.formData.value as unknown as Record<string, unknown>)[key as string] = value
}

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('identity.users', key), params)

const message = useSafeMessage()

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
 * Build the dropdown option list per-row — enable/disable + lock/unlock
 * toggle based on the row's current state. Reset password opens a modal.
 * The `delete` entry is auto-injected by TRowActions at the bottom of
 * the list (so consumers don't need to re-declare it here).
 */
function moreOptionsFor(row: UserListItem) {
  // Backend uses LockoutEnd as the single source of truth for both
  // "disabled" and "locked" states (both EnableAsync/DisableAsync and
  // LockAsync/UnlockAsync route to SetLockoutEndDateAsync). So `isLockedOut`
  // is the canonical state flag.
  return [
    { key: 'manageRoles', label: t('actions.manageRoles') },
    row.isLockedOut
      ? { key: 'enable', label: t('actions.enable') }
      : { key: 'disable', label: t('actions.disable') },
    row.isLockedOut
      ? { key: 'unlock', label: t('actions.unlock') }
      : { key: 'lock', label: t('actions.lock') },
    { key: 'resetPassword', label: t('actions.resetPassword') },
  ]
}

/** useDialog is provider-dependent (NDialogProvider). Mounts that skip
 *  the provider stack (unit tests, headless harness) would otherwise throw
 *  `[naive/use-dialog]: No outer <n-dialog-provider /> founded.` — wrap it
 *  so the confirm flow still completes in those environments. */
function safeDialog() {
  try {
    return useDialog()
  } catch {
    return {
      warning: (cfg: {
        content: string
        onPositiveClick: () => void | Promise<void>
      }) => {
        if (typeof window !== 'undefined' && typeof window.confirm === 'function') {
          if (window.confirm(cfg.content)) void cfg.onPositiveClick()
        } else {
          void cfg.onPositiveClick()
        }
      },
    } as unknown as ReturnType<typeof useDialog>
  }
}
const dialog = safeDialog()

/** Pop a `useDialog().warning(...)` confirm before invoking the action. */
function withConfirm(promptKey: string, action: () => void | Promise<void>): void {
  dialog.warning({
    title: t('actions.confirm') || 'Confirm',
    content: t(promptKey),
    positiveText: t('admin.crud.confirm'),
    negativeText: t('admin.crud.cancel'),
    onPositiveClick: async () => {
      await action()
    },
  })
}

function onMoreSelect(key: string, row: UserListItem): void {
  if (key === 'enable') withConfirm('actions.confirmEnable', () => handleEnable(row.id))
  else if (key === 'disable') withConfirm('actions.confirmDisable', () => handleDisable(row.id))
  else if (key === 'lock') void handleLock(row.id)
  else if (key === 'unlock') void handleUnlock(row.id)
  else if (key === 'resetPassword') openResetPassword(row.id)
  else if (key === 'manageRoles') void openRolesModal(row)
}

const resetPwdModal = reactive({ show: false, userId: '', password: '' })
function openResetPassword(id: string): void {
  resetPwdModal.userId = id
  resetPwdModal.password = ''
  resetPwdModal.show = true
}
async function submitResetPassword(): Promise<void> {
  const { userId, password } = resetPwdModal
  if (!userId || !password) return
  await withRefresh(() => bridge.users.resetPassword(userId, password), 'actions.resetPasswordSuccess')
  resetPwdModal.show = false
}

// ─── Manage Roles modal ───────────────────────────────────────────────────
// State for the role-assignment surface. `selectedIds` lives in modal scope
// so cancel doesn't mutate anything; `originalIds` holds the pre-edit set
// for diffing on submit. `availableRoles` is the full role catalogue,
// loaded the first time the modal opens and reused thereafter.
const availableRoles = shallowRef<RoleDto[]>([])
const rolesLoaded = ref(false)
const rolesModal = reactive({
  show: false,
  loading: false,
  saving: false,
  userId: '',
  userName: '',
  selectedIds: [] as string[],
  originalIds: [] as string[],
})
const rolesModalTitle = computed(() =>
  t('actions.manageRolesTitle', { user: rolesModal.userName || '—' }),
)

async function ensureRolesLoaded(): Promise<void> {
  if (rolesLoaded.value) return
  // Cache the role catalogue across modal opens — roles are global +
  // change rarely. RoleManagement page edits will trigger a fresh page
  // load before they'd matter here, so the cache is safe.
  availableRoles.value = await bridge.roles.getAll()
  rolesLoaded.value = true
}

async function openRolesModal(row: UserListItem): Promise<void> {
  rolesModal.userId = row.id
  rolesModal.userName = row.userName
  rolesModal.selectedIds = []
  rolesModal.originalIds = []
  rolesModal.show = true
  rolesModal.loading = true
  try {
    await ensureRolesLoaded()
    // `row.roles` carries role NAMES (per UserListItemDto). Map them to
    // ids via the freshly-loaded role catalogue. Names are case-sensitive
    // matched — backend stores the canonical name on the user-role join.
    const userRoleNames = new Set(row.roles ?? [])
    const matched = availableRoles.value
      .filter((r) => userRoleNames.has(r.name))
      .map((r) => r.id)
    rolesModal.selectedIds = matched
    rolesModal.originalIds = [...matched]
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
    rolesModal.show = false
  } finally {
    rolesModal.loading = false
  }
}

async function submitRoles(): Promise<void> {
  const { userId, selectedIds, originalIds } = rolesModal
  if (!userId) return
  rolesModal.saving = true
  try {
    await bridge.users.setRoles(userId, selectedIds, originalIds)
    message.success(t('actions.manageRolesSuccess'))
    rolesModal.show = false
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    rolesModal.saving = false
  }
}
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
