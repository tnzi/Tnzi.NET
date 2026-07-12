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

  <!--
    Direct-grants overlay (Users → More → Direct Permissions). Grants
    permission codes to ONE user without touching any role — resolution is the
    pure-allow union of role grants and direct grants (backend UserFunction).
    Renders the shared TPermissionMatrix (reusing the role page's matrix i18n
    namespace); delegation-aware graying mirrors the backend guard. A
    super-admin target renders an explainer instead — direct rows have no
    effect on members who bypass every check, and only supers may touch them.
    Deep-linked as `?grants=edit:<id>`.
  -->
  <TDetailHost :state="grantsDetail" :title="grantsTitle" :width="920" :translate="t">
    <template #default>
      <NSpin :show="grantsLoading">
        <div v-if="grantsTargetIsSuper" class="t-users-page__grants-super">
          <p class="t-users-page__hint">{{ t('grants.superTarget') }}</p>
        </div>
        <NTabs v-else v-model:value="grantsTab" type="line" size="small" animated>
          <NTabPane name="granted">
            <template #tab>
              {{ t('grants.tabGranted') }}
              <NTag v-if="grantsCheckedIds.length" size="tiny" :bordered="false" round class="t-users-page__tab-count">
                {{ grantsCheckedIds.length }}
              </NTag>
            </template>
            <p class="t-users-page__hint">{{ t('grants.hint') }}</p>
            <div class="t-users-page__grants-toolbar">
              <span class="t-users-page__grants-count">
                {{ t('grants.assignedPrefix') }} <b>{{ grantsCheckedIds.length }}</b>
              </span>
              <NTag v-if="grantsDirty" size="small" type="warning" :bordered="false" round>
                {{ t('grants.dirty', { added: grantsDirtyAdded, removed: grantsDirtyRemoved }) }}
              </NTag>
              <NInput
                v-model:value="grantsKeyword"
                size="small"
                clearable
                class="t-users-page__grants-search"
                :placeholder="t('grants.searchPlaceholder')"
              />
            </div>
            <TPermissionMatrix
              :modules="grantModules"
              :functions-by-module="grantFunctionsByModule"
              :checked-ids="grantsCheckedIds"
              :grantable-codes="grantableCodes"
              :keyword="grantsKeyword"
              :label-overrides="labelOverrides"
              expand-first
              :translate="tMatrix"
              @update:checked-ids="onGrantsChecked"
            />
          </NTabPane>
          <NTabPane name="denied">
            <template #tab>
              {{ t('grants.tabDenied') }}
              <NTag v-if="deniedCheckedIds.length" size="tiny" type="error" :bordered="false" round class="t-users-page__tab-count">
                {{ deniedCheckedIds.length }}
              </NTag>
            </template>
            <p class="t-users-page__hint">{{ t('grants.denyHint') }}</p>
            <div class="t-users-page__grants-toolbar">
              <span class="t-users-page__grants-count">
                {{ t('grants.deniedPrefix') }} <b>{{ deniedCheckedIds.length }}</b>
              </span>
              <NTag v-if="deniedDirty" size="small" type="warning" :bordered="false" round>
                {{ t('grants.dirty', { added: deniedDirtyAdded, removed: deniedDirtyRemoved }) }}
              </NTag>
              <NInput
                v-model:value="grantsKeyword"
                size="small"
                clearable
                class="t-users-page__grants-search"
                :placeholder="t('grants.searchPlaceholder')"
              />
            </div>
            <TPermissionMatrix
              :modules="grantModules"
              :functions-by-module="grantFunctionsByModule"
              :checked-ids="deniedCheckedIds"
              :grantable-codes="grantableCodes"
              :keyword="grantsKeyword"
              :label-overrides="labelOverrides"
              expand-first
              :translate="tMatrix"
              @update:checked-ids="onDeniedChecked"
            />
          </NTabPane>
        </NTabs>
      </NSpin>
    </template>
    <template #footer="{ close }">
      <NButton @click="close">{{ t('admin.crud.cancel') }}</NButton>
      <NButton
        v-if="canAssignGrants && !grantsTargetIsSuper"
        type="primary"
        :loading="grantsSaving"
        :disabled="grantsLoading || !anyGrantsDirty"
        @click="submitGrants"
      >
        {{ t('admin.crud.confirm') }}
      </NButton>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TPermissionMatrix from '../../components/forms/TPermissionMatrix.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import TFormSchemaRenderer from '../_shared/form-schema'
import { userColumns, userSearchFields, userFormSchema } from './user-config'
import { ZH_SURFACE_LABELS } from '../authorization/surface-labels'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { computed, ref, shallowRef, watch } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpin, NCheckbox, NCheckboxGroup, NTag, NTabs, NTabPane } from 'naive-ui'
import { useSafeMessage } from '../_shared/safeMessage'
import type { RoleDto } from '@tnzi/core/services/identity'
import type { FunctionModuleDto, ModuleFunctionDto } from '@tnzi/core/services/authorization'

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

// ─── Direct-grants overlay (UserFunction) ─────────────────────────────────
const authBridge = createAuthorizationBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()
const canViewGrants = computed(() => can('authorization.userFunction.view'))
const canAssignGrants = computed(() => can('authorization.userFunction.assign'))

const grantsDetail = useDetail<UserListItem>({
  mode: 'drawer',
  url: 'grants',
  source: crud,
})
const grantsUser = computed(() => grantsDetail.data.value)
const grantsTitle = computed(() =>
  t('grants.title', { user: grantsUser.value?.userName || '—' }),
)

const grantsLoading = ref(false)
const grantsSaving = ref(false)
const grantsKeyword = ref('')
const grantsTab = ref<'granted' | 'denied'>('granted')
const grantsCheckedIds = ref<string[]>([])
const grantsOriginalIds = ref<Set<string>>(new Set())
const deniedCheckedIds = ref<string[]>([])
const deniedOriginalIds = ref<Set<string>>(new Set())

// The two sets are mutually exclusive per function (one row per (user,
// function) on the backend — allow XOR deny). Mirror that in the UI: ticking
// a function on one tab silently unticks it on the other.
function onGrantsChecked(ids: string[]): void {
  grantsCheckedIds.value = ids
  const allowSet = new Set(ids)
  deniedCheckedIds.value = deniedCheckedIds.value.filter((id) => !allowSet.has(id))
}

function onDeniedChecked(ids: string[]): void {
  deniedCheckedIds.value = ids
  const denySet = new Set(ids)
  grantsCheckedIds.value = grantsCheckedIds.value.filter((id) => !denySet.has(id))
}

// Permission catalogue (modules + functions), loaded once on first open and
// reused — it is global and changes rarely, mirroring the role page.
const grantModules = shallowRef<FunctionModuleDto[]>([])
const grantFunctionsByModule = shallowRef(new Map<string, ModuleFunctionDto[]>())
const catalogueLoaded = ref(false)

// Delegation-aware graying (mirrors the backend guard): a non-super grantor
// may only hand out codes from their own set. null = everything grantable.
const authStore = useAdminAuthStore()
const grantableCodes = computed<string[] | null>(() =>
  authStore.isSuperUser || authStore.userInfo === null ? null : authStore.userPermissions,
)

// The role matrix i18n namespace already carries every matrix.* key in both
// locales — reuse it instead of duplicating the strings under identity.users.
const tMatrix = makePageTranslator('authorization.roleFunctions')
const appStore = useAdminAppStore()
const labelOverrides = computed(() => (appStore.locale === 'zh-cn' ? ZH_SURFACE_LABELS : null))

// Direct rows have no effect on super-admin members (they bypass every
// check) and the backend guard rejects non-super writes on them — render an
// explainer instead of an editable matrix. Best-effort: role names come from
// the list row; the super-role list from the read endpoint (empty on older
// backends → normal rendering, the backend guard still enforces).
const superRoleNames = ref<Set<string>>(new Set())
const grantsTargetIsSuper = computed(() => {
  const roles = grantsUser.value?.roles ?? []
  return roles.some((name) => superRoleNames.value.has(name.toLowerCase()))
})

async function ensureCatalogueLoaded(): Promise<void> {
  if (catalogueLoaded.value) return
  const modules = await authBridge.functionModules.getAll()
  const next = new Map<string, ModuleFunctionDto[]>()
  await Promise.all(
    modules.map(async (m) => {
      try {
        next.set(m.id, await authBridge.permissions.getByModule(m.id))
      } catch {
        next.set(m.id, [])
      }
    }),
  )
  grantModules.value = modules
  grantFunctionsByModule.value = next
  catalogueLoaded.value = true
  try {
    const names = await authBridge.roleFunctions.superAdminRoles()
    superRoleNames.value = new Set(names.map((n) => n.toLowerCase()))
  } catch {
    superRoleNames.value = new Set()
  }
}

// Load catalogue + the user's direct grants/denies whenever the overlay
// binds to a user (in-session open OR a `?grants=edit:<id>` deep link).
watch(() => grantsDetail.data.value, async (user) => {
  if (!user) return
  grantsLoading.value = true
  grantsKeyword.value = ''
  grantsTab.value = 'granted'
  grantsCheckedIds.value = []
  grantsOriginalIds.value = new Set()
  deniedCheckedIds.value = []
  deniedOriginalIds.value = new Set()
  try {
    await ensureCatalogueLoaded()
    const [ids, deniedIds] = await Promise.all([
      authBridge.userFunctions.getAssignedIds(user.id),
      authBridge.userFunctions.getDeniedIds(user.id),
    ])
    grantsCheckedIds.value = [...ids]
    grantsOriginalIds.value = new Set(ids)
    deniedCheckedIds.value = [...deniedIds]
    deniedOriginalIds.value = new Set(deniedIds)
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
    grantsDetail.close()
  } finally {
    grantsLoading.value = false
  }
})

function setDirty(checked: string[], original: Set<string>): boolean {
  if (original.size !== checked.length) return true
  return checked.some((id) => !original.has(id))
}
function countAdded(checked: string[], original: Set<string>): number {
  return checked.filter((id) => !original.has(id)).length
}
function countRemoved(checked: string[], original: Set<string>): number {
  const set = new Set(checked)
  let n = 0
  for (const id of original) {
    if (!set.has(id)) n += 1
  }
  return n
}

const grantsDirty = computed(() => setDirty(grantsCheckedIds.value, grantsOriginalIds.value))
const grantsDirtyAdded = computed(() => countAdded(grantsCheckedIds.value, grantsOriginalIds.value))
const grantsDirtyRemoved = computed(() => countRemoved(grantsCheckedIds.value, grantsOriginalIds.value))
const deniedDirty = computed(() => setDirty(deniedCheckedIds.value, deniedOriginalIds.value))
const deniedDirtyAdded = computed(() => countAdded(deniedCheckedIds.value, deniedOriginalIds.value))
const deniedDirtyRemoved = computed(() => countRemoved(deniedCheckedIds.value, deniedOriginalIds.value))
const anyGrantsDirty = computed(() => grantsDirty.value || deniedDirty.value)

async function submitGrants(): Promise<void> {
  const user = grantsDetail.data.value
  if (!user) return
  grantsSaving.value = true
  try {
    // Save the deny set first so a grant→deny move never passes through a
    // transient state where the code is granted with the deny not yet saved.
    if (deniedDirty.value) {
      await authBridge.userFunctions.setDeniedForUser(user.id, deniedCheckedIds.value)
      deniedOriginalIds.value = new Set(deniedCheckedIds.value)
    }
    if (grantsDirty.value) {
      await authBridge.userFunctions.setForUser(user.id, grantsCheckedIds.value)
      grantsOriginalIds.value = new Set(grantsCheckedIds.value)
    }
    message.success(t('grants.success'))
    grantsDetail.close()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    grantsSaving.value = false
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
  { key: 'manageRoles', label: 'actions.manageRoles', show: () => crud.canUpdate, onClick: (row) => void rolesDetail.open('edit', row) },
  { key: 'directGrants', label: 'actions.managePermissions', show: () => canViewGrants.value, onClick: (row) => void grantsDetail.open('edit', row) },
  { key: 'enable', label: 'actions.enable', show: (row) => crud.canUpdate && row.isLockedOut === true, confirm: 'actions.confirmEnable', onClick: (row) => void handleEnable(row.id) },
  { key: 'disable', label: 'actions.disable', show: (row) => crud.canUpdate && row.isLockedOut !== true, confirm: 'actions.confirmDisable', onClick: (row) => void handleDisable(row.id) },
  { key: 'unlock', label: 'actions.unlock', show: (row) => crud.canUpdate && row.isLockedOut === true, onClick: (row) => void handleUnlock(row.id) },
  { key: 'lock', label: 'actions.lock', show: (row) => crud.canUpdate && row.isLockedOut !== true, onClick: (row) => void handleLock(row.id) },
  { key: 'resetPassword', label: 'actions.resetPassword', show: () => crud.canUpdate, onClick: (row) => void resetPwdDetail.open('edit', row) },
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
/* Direct-grants drawer: count + dirty chip on the left, keyword filter on
   the right, matrix below. */
.t-users-page__grants-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
}
.t-users-page__grants-count {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
  white-space: nowrap;
}
.t-users-page__grants-search {
  margin-left: auto;
  max-width: 240px;
}
.t-users-page__grants-super {
  padding: 24px 8px;
}
.t-users-page__tab-count {
  margin-left: 6px;
}
</style>
