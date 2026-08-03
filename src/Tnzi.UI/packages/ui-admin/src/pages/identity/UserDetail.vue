<template>
  <TDetailLayout
    layout="side"
    :sections="sections"
    v-model:active-section="section"
    :back="backTarget"
    :translate="t"
  >
    <template #title>
      <span class="ud-title">{{ user?.userName || t('detail.loading') }}</span>
    </template>

    <template #actions>
      <template v-if="user && canUpdate">
        <NButton size="small" tertiary @click="openResetPassword">
          <template #icon><TSvgIcon icon="mdi:lock-reset" :size="15" /></template>
          {{ t('actions.resetPassword') }}
        </NButton>
        <NPopconfirm @positive-click="toggleLock">
          <template #trigger>
            <NButton size="small" tertiary :type="user.isLockedOut ? 'success' : 'warning'">
              <template #icon>
                <TSvgIcon :icon="user.isLockedOut ? 'mdi:lock-open-variant-outline' : 'mdi:lock-outline'" :size="15" />
              </template>
              {{ user.isLockedOut ? t('actions.unlock') : t('actions.lock') }}
            </NButton>
          </template>
          {{ user.isLockedOut ? t('actions.confirmEnable') : t('actions.confirmDisable') }}
        </NPopconfirm>
      </template>
    </template>

    <template #default="{ section: sec }">
      <div class="ud-panel">
        <NSpin :show="loading">
          <!--
            Profile: the identity band answers "who is this" before any field,
            then the editable record underneath.

            No `max-width="none"` here on purpose - a FORM section keeps
            TDetailSection's default 920px cap so inputs don't stretch
            edge-to-edge on a wide display (an 1800px-wide text box is unusable
            and unreadable). The sibling sections below are lists / grids /
            matrices, which DO opt out of the cap. Same split as User Center:
            `contained` for forms, `:contained="false"` for tables.
          -->
          <TDetailSection v-if="sec === 'profile'" :title="t('detail.sections.profile')">
            <template v-if="user">
              <TRecordHeader
                class="ud-header"
                :name="displayName"
                :subtitle="user.userName"
                :avatar="user.avatar ?? undefined"
                :avatar-name="displayName"
                :badges="badges"
                :facts="facts"
              />
              <TFormSchemaRenderer
                :schema="userProfileSchema"
                :sections="userProfileSections"
                :model="form"
                :readonly="!canUpdate"
                :translate="t"
              />
            </template>

            <template v-if="user && canUpdate" #savebar>
              <NButton size="small" type="primary" :loading="saving" @click="save">
                <template #icon><TSvgIcon icon="mdi:content-save-outline" :size="15" /></template>
                {{ t('admin.common.save') }}
              </NButton>
            </template>
          </TDetailSection>

          <UserRolesSection
            v-else-if="sec === 'roles' && user"
            :user-id="id"
            :user-role-names="user.roles ?? []"
            :can-edit="canUpdate"
            :t="t"
            @saved="reload"
          />

          <UserGrantsSection
            v-else-if="sec === 'grants' && user"
            :user-id="id"
            :user-role-names="user.roles ?? []"
            :can-assign="canAssignGrants"
            :t="t"
          />

          <UserSessionsSection
            v-else-if="sec === 'sessions' && user"
            :user-id="id"
            :can-revoke="canUpdate"
            :t="t"
          />

          <UserLoginLogsSection v-else-if="sec === 'loginLogs' && user" :user-id="id" :t="t" />
        </NSpin>
      </div>
    </template>
  </TDetailLayout>

  <TModalShell v-model:show="resetPwdShow" :title="t('actions.resetPassword')" :width="440">
    <NForm label-placement="top" :show-feedback="false">
      <NFormItem :label="t('actions.newPassword')" required>
        <NInput
          v-model:value="resetPwdValue"
          type="password"
          show-password-on="click"
          :placeholder="t('actions.resetPasswordHint')"
        />
      </NFormItem>
    </NForm>
    <template #footer>
      <NButton @click="resetPwdShow = false">{{ t('admin.common.cancel') }}</NButton>
      <NButton type="primary" :loading="resetPwdSaving" :disabled="!resetPwdValue" @click="submitResetPassword">
        {{ t('admin.common.confirm') }}
      </NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * One user's page.
 *
 * A user is not five columns in a grid: they have a profile, a set of roles,
 * permissions granted to them personally, devices they are signed in on, and a
 * sign-in history. Those used to be four overlays hanging off a table row,
 * which meant an admin investigating an account opened and closed four modals
 * and could never see two facts side by side. Here they are five sections of
 * one page, deep-linkable via `?section=`, with the account's identity band
 * pinned at the top of the profile.
 */
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NForm, NFormItem, NInput, NPopconfirm, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TDetailLayout from '../../components/detail/TDetailLayout.vue'
import TDetailSection from '../../components/detail/TDetailSection.vue'
import TRecordHeader, { type RecordBadge, type RecordFact } from '../../components/detail/TRecordHeader.vue'
import TModalShell from '../../components/overlay/TModalShell.vue'
import TFormSchemaRenderer from '../_shared/form-schema'
import UserRolesSection from './sections/UserRolesSection.vue'
import UserGrantsSection from './sections/UserGrantsSection.vue'
import UserSessionsSection from './sections/UserSessionsSection.vue'
import UserLoginLogsSection from './sections/UserLoginLogsSection.vue'
import { userProfileSchema, userProfileSections } from './user-config'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { useTabTitle } from '../../headless/useTabTitle'
import { useBreadcrumbLabel } from '../../headless/use-breadcrumb'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { formatDateOnly } from '@tnzi/core/utils'
import type { UserDto } from '@tnzi/core/services/identity'

const props = defineProps<{ id: string }>()

const t = makePageTranslator('identity.users')
const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useSafeMessage()
const { can } = usePermissionGuard()

const canUpdate = computed(() => can('user.update'))
const canViewGrants = computed(() => can('authorization.userFunction.view'))
const canAssignGrants = computed(() => can('authorization.userFunction.assign'))

/**
 * Smart back: step through the in-app history when there is one (so the list
 * comes back with its own paging / search state), else resolve the list route BY
 * NAME.
 *
 * Resolving by name is the part that matters: a hard-coded `/identity/users`
 * 404s because every admin route lives under the shell prefix, and a hard-coded
 * `/admin/identity/users` breaks the moment a consumer passes a custom
 * `basePath` to `defineAdminApp`. `router.resolve` applies whatever prefix is in
 * effect.
 */
const router = useRouter()
const backTarget = computed(() => ({ fallback: router.resolve({ name: 'identity.users' }).path }))

const user = ref<UserDto | null>(null)
const loading = ref(true)
const saving = ref(false)
const form = reactive<Record<string, unknown>>({})

const displayName = computed(() => {
  const u = user.value
  if (!u) return ''
  const full = [u.firstName, u.lastName].filter(Boolean).join(' ')
  return u.nickname || full || u.userName
})

// The tab + breadcrumb leaf carry the user's name instead of the static route
// title every user detail tab would otherwise share.
useTabTitle(() => (user.value ? displayName.value : null))
useBreadcrumbLabel(() => (user.value ? displayName.value : null))

async function reload(): Promise<void> {
  loading.value = true
  try {
    const dto = await bridge.users.getById(props.id)
    user.value = dto
    Object.assign(form, {
      email: dto.email ?? null,
      phoneNumber: dto.phoneNumber ?? null,
      nickname: dto.nickname ?? null,
      firstName: dto.firstName ?? null,
      lastName: dto.lastName ?? null,
      gender: dto.gender ?? 0,
      birthday: dto.birthday ?? null,
      address: dto.address ?? null,
      website: dto.website ?? null,
      bio: dto.bio ?? null,
    })
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    loading.value = false
  }
}
void reload()
watch(() => props.id, () => void reload())

const badges = computed<RecordBadge[]>(() => {
  const u = user.value
  if (!u) return []
  const out: RecordBadge[] = [
    u.isLockedOut
      ? { label: t('admin.shared.status.locked'), type: 'error' }
      : { label: t('admin.shared.status.active'), type: 'success' },
  ]
  if (u.twoFactorEnabled) out.push({ label: t('columns.twoFactorEnabled'), type: 'info' })
  if (!u.isEmailConfirmed) out.push({ label: t('admin.shared.status.unconfirmed'), type: 'warning' })
  return out
})

const facts = computed<RecordFact[]>(() => {
  const u = user.value
  if (!u) return []
  return [
    { icon: 'mdi:email-outline', value: u.email ?? '' },
    { icon: 'mdi:phone-outline', value: u.phoneNumber ?? '' },
    { icon: 'mdi:office-building-outline', value: u.organizationName ?? '' },
    { icon: 'mdi:account-key-outline', value: (u.roles ?? []).join(', ') },
    { icon: 'mdi:calendar-plus', value: formatDateOnly(u.creationTime) },
  ]
})

/** Grants only appear for admins allowed to read them. */
const sections = computed(() => {
  const list = [
    { key: 'profile', label: t('detail.sections.profile'), icon: 'mdi:account-outline', group: t('detail.groups.general') },
    { key: 'roles', label: t('detail.sections.roles'), icon: 'mdi:account-key-outline', group: t('detail.groups.access') },
  ]
  if (canViewGrants.value) {
    list.push({ key: 'grants', label: t('detail.sections.grants'), icon: 'mdi:shield-key-outline', group: t('detail.groups.access') })
  }
  list.push(
    { key: 'sessions', label: t('detail.sections.sessions'), icon: 'mdi:devices', group: t('detail.groups.activity') },
    { key: 'loginLogs', label: t('detail.sections.loginLogs'), icon: 'mdi:history', group: t('detail.groups.activity') },
  )
  return list
})

// Two-way bound to `?section=` so a deep link (and Back/Forward) selects the
// panel, matching every other detail page in the framework.
//
// `sections` is passed as the COMPUTED, not `sections.value`: the grants panel
// only appears once the permission probe resolves, and a snapshot taken at setup
// would not contain it - a `?section=grants` deep link would then be rejected as
// an unknown section and silently fall back to Profile. `useDetail` accepts a
// ref/getter and re-resolves when the list changes.
const detail = useDetail({ mode: 'page', sectionUrl: true, sections, defaultSection: 'profile' })
const section = computed({
  get: () => detail.activeSection.value ?? 'profile',
  set: (v: string) => detail.setSection(v),
})

async function save(): Promise<void> {
  saving.value = true
  try {
    await bridge.users.update(props.id, form as never)
    message.success(t('detail.saved'))
    await reload()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    saving.value = false
  }
}

async function toggleLock(): Promise<void> {
  const u = user.value
  if (!u) return
  try {
    if (u.isLockedOut) await bridge.users.unlock(props.id)
    else await bridge.users.lock(props.id)
    message.success(t(u.isLockedOut ? 'actions.unlockSuccess' : 'actions.lockSuccess'))
    await reload()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

const resetPwdShow = ref(false)
const resetPwdValue = ref('')
const resetPwdSaving = ref(false)
function openResetPassword(): void {
  resetPwdValue.value = ''
  resetPwdShow.value = true
}
async function submitResetPassword(): Promise<void> {
  resetPwdSaving.value = true
  try {
    await bridge.users.resetPassword(props.id, resetPwdValue.value)
    message.success(t('actions.resetPasswordSuccess'))
    resetPwdShow.value = false
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    resetPwdSaving.value = false
  }
}
</script>

<style scoped>
.ud-title {
  font-size: 18px;
  font-weight: 700;
  color: var(--tnzi-base-text);
}
.ud-panel {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.ud-panel :deep(.n-spin-container),
.ud-panel :deep(.n-spin-content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
.ud-header {
  margin-bottom: 18px;
}
</style>
