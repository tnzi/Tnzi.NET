<template>
  <!--
    UserCenter — self-service personal center.

    Left-rail tabs route between sections, right pane renders the active
    section. Each section is rendered inline (single .vue file) so consumers
    don't have to register five sub-routes to get a working profile page.

    Wires the `identityBridge.me.*` sub-contract added alongside this page —
    see `services/bridges/identity-bridge.ts` for the surface.
  -->
  <div class="t-user-center t-stack-page">
    <NCard :title="t('title')" :bordered="false">
      <template #header-extra>
        <NButton size="small" tertiary :loading="loadingProfile" @click="reloadAll">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('refresh') }}
        </NButton>
      </template>

      <div class="t-user-center__layout t-form-page">
        <!-- Left rail — tab nav -->
        <NMenu
          v-model:value="activeTab"
          :options="navOptions"
          class="t-user-center__nav"
        />

        <!-- Right content -->
        <section class="t-user-center__content">
          <NSpin :show="loadingProfile && !profile">
            <!-- ===== Basic profile ===== -->
            <div v-if="activeTab === 'profile'" class="t-user-center__section t-user-center__section--scroll">
              <header class="t-user-center__section-header">
                <div class="t-user-center__identity">
                  <!--
                    NAvatar inside the admin shell rendered as 0x0 (CSS conflict
                    with the shell's flex tokens). Use a plain `<div>` we
                    fully control: shows the user's uploaded avatar URL if
                    present, otherwise an initial letter on a tinted disc.
                  -->
                  <div class="t-user-center__avatar">
                    <img
                      v-if="profile?.avatar"
                      :src="profile.avatar"
                      :alt="profile.userName"
                      class="t-user-center__avatar-img"
                    />
                    <span v-else class="t-user-center__avatar-initials">
                      {{ avatarInitials }}
                    </span>
                  </div>
                  <div>
                    <h2 class="t-user-center__user-name">
                      {{ profile?.nickname || profile?.userName || '—' }}
                    </h2>
                    <div class="t-user-center__user-meta">
                      <NTag v-for="r in profile?.roles ?? []" :key="r" size="small" :bordered="false">
                        {{ r }}
                      </NTag>
                      <span v-if="!(profile?.roles?.length)" class="t-user-center__hint">
                        {{ t('profile.noRoles') }}
                      </span>
                    </div>
                  </div>
                </div>
              </header>

              <NForm label-placement="top" size="small" :show-feedback="false">
                <div class="t-user-center__form-grid">
                  <NFormItem :label="t('profile.userName')">
                    <NInput :value="profile?.userName ?? ''" disabled />
                  </NFormItem>
                  <NFormItem :label="t('profile.nickname')">
                    <NInput v-model:value="form.nickname" :placeholder="t('profile.nicknamePlaceholder')" />
                  </NFormItem>
                  <!--
                    Email / phone are NOT directly editable via PUT — the
                    backend exposes a send-code + confirm flow (so a typo
                    can't lock the user out, and we get verification).
                    Render as read-only inputs with a "Change…" trigger
                    that opens the verification modal.
                  -->
                  <NFormItem :label="t('profile.email')">
                    <NInput
                      :value="profile?.email ?? ''"
                      disabled
                      :placeholder="t('profile.emailPlaceholder')"
                    >
                      <template #suffix>
                        <NButton size="tiny" tertiary @click="openChangeEmail">
                          {{ t('profile.change') }}
                        </NButton>
                      </template>
                    </NInput>
                  </NFormItem>
                  <NFormItem :label="t('profile.phone')">
                    <NInput
                      :value="profile?.phoneNumber ?? ''"
                      disabled
                      :placeholder="t('profile.phonePlaceholder')"
                    >
                      <template #suffix>
                        <NButton size="tiny" tertiary @click="openChangePhone">
                          {{ t('profile.change') }}
                        </NButton>
                      </template>
                    </NInput>
                  </NFormItem>
                  <NFormItem :label="t('profile.gender')">
                    <NSelect
                      v-model:value="form.gender"
                      :options="genderOptions"
                      :placeholder="t('profile.genderPlaceholder')"
                    />
                  </NFormItem>
                  <NFormItem :label="t('profile.birthday')">
                    <NDatePicker
                      v-model:formatted-value="form.birthday"
                      value-format="yyyy-MM-dd"
                      type="date"
                      clearable
                      style="width: 100%"
                    />
                  </NFormItem>
                </div>
                <NFormItem :label="t('profile.bio')">
                  <NInput
                    v-model:value="form.bio"
                    type="textarea"
                    :rows="3"
                    :placeholder="t('profile.bioPlaceholder')"
                  />
                </NFormItem>
                <NFormItem :label="t('profile.address')">
                  <NInput v-model:value="form.address" :placeholder="t('profile.addressPlaceholder')" />
                </NFormItem>
                <NFormItem :label="t('profile.website')">
                  <NInput v-model:value="form.website" :placeholder="t('profile.websitePlaceholder')" />
                </NFormItem>
              </NForm>

              <div class="t-user-center__actions">
                <NButton type="primary" :loading="savingProfile" @click="saveProfile">
                  {{ t('save') }}
                </NButton>
                <NButton @click="resetProfileForm">{{ t('reset') }}</NButton>
              </div>
            </div>

            <!-- ===== Security ===== -->
            <div v-else-if="activeTab === 'security'" class="t-user-center__section t-user-center__section--scroll">
              <h3 class="t-user-center__section-title">{{ t('security.password.title') }}</h3>
              <p class="t-user-center__hint">{{ t('security.password.hint') }}</p>
              <NForm label-placement="top" size="small" :show-feedback="false">
                <div class="t-user-center__form-grid">
                  <NFormItem :label="t('security.password.current')" required>
                    <NInput
                      v-model:value="pwForm.currentPassword"
                      type="password"
                      show-password-on="click"
                      :placeholder="t('security.password.currentPlaceholder')"
                    />
                  </NFormItem>
                  <NFormItem :label="t('security.password.new')" required>
                    <NInput
                      v-model:value="pwForm.newPassword"
                      type="password"
                      show-password-on="click"
                      :placeholder="t('security.password.newPlaceholder')"
                    />
                  </NFormItem>
                  <NFormItem :label="t('security.password.confirm')" required>
                    <NInput
                      v-model:value="pwForm.confirm"
                      type="password"
                      show-password-on="click"
                      :placeholder="t('security.password.confirmPlaceholder')"
                    />
                  </NFormItem>
                </div>
              </NForm>
              <div class="t-user-center__actions">
                <NButton
                  type="primary"
                  :loading="changingPassword"
                  :disabled="!canChangePassword"
                  @click="submitChangePassword"
                >
                  {{ t('security.password.submit') }}
                </NButton>
              </div>

              <NDivider />

              <h3 class="t-user-center__section-title">{{ t('security.twoFactor.title') }}</h3>
              <div class="t-user-center__row">
                <div>
                  <div class="t-user-center__row-label">{{ t('security.twoFactor.totp') }}</div>
                  <div class="t-user-center__hint">{{ t('security.twoFactor.totpHint') }}</div>
                </div>
                <NSpace size="small" align="center">
                  <NTag :type="twoFactor?.isTotpEnabled ? 'success' : 'default'" size="small" :bordered="false">
                    {{ twoFactor?.isTotpEnabled ? t('security.twoFactor.enabled') : t('security.twoFactor.disabled') }}
                  </NTag>
                  <NPopconfirm v-if="twoFactor?.isTotpEnabled" @positive-click="disableTotp">
                    <template #trigger>
                      <NButton size="tiny" type="warning" ghost :loading="togglingTwoFactor">
                        {{ t('security.twoFactor.disable') }}
                      </NButton>
                    </template>
                    {{ t('security.twoFactor.confirmDisable') }}
                  </NPopconfirm>
                </NSpace>
              </div>
              <div class="t-user-center__row">
                <div>
                  <div class="t-user-center__row-label">{{ t('security.twoFactor.global') }}</div>
                  <div class="t-user-center__hint">{{ t('security.twoFactor.globalHint') }}</div>
                </div>
                <NSpace size="small" align="center">
                  <NTag :type="twoFactor?.isEnabled ? 'success' : 'warning'" size="small" :bordered="false">
                    {{ twoFactor?.isEnabled ? t('security.twoFactor.enabled') : t('security.twoFactor.disabled') }}
                  </NTag>
                  <NPopconfirm v-if="twoFactor?.isEnabled" @positive-click="disableTwoFactor">
                    <template #trigger>
                      <NButton size="tiny" type="warning" ghost :loading="togglingTwoFactor">
                        {{ t('security.twoFactor.disable') }}
                      </NButton>
                    </template>
                    {{ t('security.twoFactor.confirmDisable') }}
                  </NPopconfirm>
                </NSpace>
              </div>
            </div>

            <!-- ===== Sessions (login devices) =====
                 Table-section variant: the NDataTable owns its own scroll
                 + pagination so the outer page never scrolls. -->
            <div v-else-if="activeTab === 'sessions'" class="t-user-center__section t-user-center__section--table">
              <header class="t-user-center__section-header">
                <h3 class="t-user-center__section-title">{{ t('sessions.title') }}</h3>
                <NPopconfirm @positive-click="revokeAll">
                  <template #trigger>
                    <NButton size="small" type="error" ghost :disabled="!sessions.length">
                      {{ t('sessions.revokeAll') }}
                    </NButton>
                  </template>
                  {{ t('sessions.confirmRevokeAll') }}
                </NPopconfirm>
              </header>
              <p class="t-user-center__hint">{{ t('sessions.hint') }}</p>
              <NDataTable
                :data="sessions"
                :columns="sessionColumns"
                :row-key="(r: UserSessionDto) => r.id"
                size="small"
                :bordered="false"
                :loading="loadingSessions"
                :flex-height="true"
                :pagination="{ pageSize: 10 }"
              />
            </div>

            <!-- ===== Login history =====
                 Same table-section variant — flex-fill + built-in pager. -->
            <div v-else-if="activeTab === 'history'" class="t-user-center__section t-user-center__section--table">
              <header class="t-user-center__section-header">
                <h3 class="t-user-center__section-title">{{ t('history.title') }}</h3>
                <NButton size="small" tertiary :loading="loadingHistory" @click="loadHistory">
                  <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
                  {{ t('refresh') }}
                </NButton>
              </header>
              <NDataTable
                :data="history"
                :columns="historyColumns"
                :row-key="(r: LoginLogDto) => r.id"
                size="small"
                :bordered="false"
                :loading="loadingHistory"
                :flex-height="true"
                :pagination="{ pageSize: 15 }"
              />
            </div>

            <!-- ===== Linked accounts ===== -->
            <div v-else-if="activeTab === 'linked'" class="t-user-center__section t-user-center__section--scroll">
              <h3 class="t-user-center__section-title">{{ t('linked.title') }}</h3>
              <p class="t-user-center__hint">{{ t('linked.hint') }}</p>
              <NSpin :show="loadingLinked">
                <ul v-if="linked.length" class="t-user-center__linked-list">
                  <li v-for="acc in linked" :key="acc.loginProvider" class="t-user-center__linked-item">
                    <div>
                      <div class="t-user-center__row-label">{{ acc.providerDisplayName || acc.loginProvider }}</div>
                      <div class="t-user-center__hint">{{ acc.providerKey }}</div>
                    </div>
                    <NPopconfirm @positive-click="unlink(acc.loginProvider)">
                      <template #trigger>
                        <NButton size="small" type="error" ghost>{{ t('linked.unlink') }}</NButton>
                      </template>
                      {{ t('linked.confirmUnlink') }}
                    </NPopconfirm>
                  </li>
                </ul>
                <div v-else class="t-user-center__empty">{{ t('linked.empty') }}</div>
              </NSpin>
            </div>

            <!-- ===== Danger zone ===== -->
            <div v-else-if="activeTab === 'danger'" class="t-user-center__section t-user-center__section--scroll">
              <h3 class="t-user-center__section-title">{{ t('danger.title') }}</h3>
              <p class="t-user-center__hint">{{ t('danger.hint') }}</p>

              <NAlert type="info" :show-icon="false" style="margin-bottom: 12px">
                <strong>{{ t('danger.export.title') }}</strong>
                <p class="t-user-center__hint">{{ t('danger.export.hint') }}</p>
                <NButton size="small" :loading="exporting" @click="exportData">
                  <template #icon><TSvgIcon icon="mdi:download" :size="14" /></template>
                  {{ t('danger.export.button') }}
                </NButton>
              </NAlert>

              <NAlert type="warning" :show-icon="false" style="margin-bottom: 12px">
                <strong>{{ t('danger.deactivate.title') }}</strong>
                <p class="t-user-center__hint">{{ t('danger.deactivate.hint') }}</p>
                <NPopconfirm @positive-click="deactivateAccount">
                  <template #trigger>
                    <NButton size="small" type="warning" ghost :loading="deactivating">
                      {{ t('danger.deactivate.button') }}
                    </NButton>
                  </template>
                  {{ t('danger.deactivate.confirm') }}
                </NPopconfirm>
              </NAlert>

              <NAlert type="error" :show-icon="false">
                <strong>{{ t('danger.delete.title') }}</strong>
                <p class="t-user-center__hint">{{ t('danger.delete.hint') }}</p>
                <NPopconfirm @positive-click="deleteAccount">
                  <template #trigger>
                    <NButton size="small" type="error" :loading="deleting">
                      {{ t('danger.delete.button') }}
                    </NButton>
                  </template>
                  {{ t('danger.delete.confirm') }}
                </NPopconfirm>
              </NAlert>
            </div>
          </NSpin>
        </section>
      </div>
    </NCard>

    <!-- Change email / phone (two-step verify) -->
    <NModal v-model:show="changeModal.show" preset="card" style="width: 480px">
      <template #header>
        <span>{{ changeModalTitle }}</span>
      </template>
      <NForm label-placement="top" size="small" :show-feedback="false">
        <NFormItem :label="t(changeModal.kind === 'email' ? 'changeModal.newEmail' : 'changeModal.newPhone')" required>
          <NInput
            v-model:value="changeModal.target"
            :disabled="changeModal.step === 'confirm'"
            :placeholder="t(changeModal.kind === 'email' ? 'changeModal.newEmailPlaceholder' : 'changeModal.newPhonePlaceholder')"
          />
        </NFormItem>
        <NFormItem v-if="changeModal.step === 'confirm'" :label="t('changeModal.code')" required>
          <NInput v-model:value="changeModal.code" :placeholder="t('changeModal.codePlaceholder')" />
        </NFormItem>
        <p class="t-user-center__hint">{{ t('changeModal.hint') }}</p>
      </NForm>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 8px">
          <NButton @click="changeModal.show = false">{{ t('changeModal.cancel') }}</NButton>
          <NButton
            v-if="changeModal.step === 'send'"
            type="primary"
            :loading="changeModal.sending"
            :disabled="!changeModal.target.trim()"
            @click="sendChangeCode"
          >
            {{ t('changeModal.sendCode') }}
          </NButton>
          <NButton
            v-else
            type="primary"
            :loading="changeModal.confirming"
            :disabled="!changeModal.code.trim()"
            @click="confirmChange"
          >
            {{ t('changeModal.confirm') }}
          </NButton>
        </div>
      </template>
    </NModal>
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { DataTableColumns, MenuOption } from 'naive-ui'
import {
  NAlert,
  NButton,
  NCard,
  NDataTable,
  NDatePicker,
  NDivider,
  NForm,
  NFormItem,
  NInput,
  NMenu,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSpin,
  NTag,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useSafeMessage } from '../_shared/safeMessage'
import { deviceIconColor, parseDeviceInfo } from '../_shared/device-info'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { makePageTranslator } from '../_shared/translate'
import type {
  UserDto,
  UserDetailDto,
  UserSessionDto,
  LoginLogDto,
  UserLoginDto,
  TwoFactorStatusDto,
  UpdateUserDto,
} from '@tnzi/core/services/identity'

const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useSafeMessage()
const router = useRouter()
const authStore = useAdminAuthStore()
const t = makePageTranslator('account.userCenter')

/**
 * Common post-action when the current user's session becomes invalid
 * (deactivate / delete / revoke all sessions). Clears auth state, drops
 * any cached state, and hard-navigates to login so the next API call
 * doesn't 401 against a half-rendered profile.
 */
function logoutAndRedirect(): void {
  authStore.logout()
  void router.replace({ name: 'login' }).catch(() => undefined)
}

// ---- Active tab -----------------------------------------------------------
const activeTab = ref<'profile' | 'security' | 'sessions' | 'history' | 'linked' | 'danger'>(
  'profile',
)

const navOptions = computed<MenuOption[]>(() => [
  { key: 'profile', label: t('nav.profile'), icon: () => h(TSvgIcon, { icon: 'mdi:account-outline', size: 16 }) },
  { key: 'security', label: t('nav.security'), icon: () => h(TSvgIcon, { icon: 'mdi:shield-lock-outline', size: 16 }) },
  { key: 'sessions', label: t('nav.sessions'), icon: () => h(TSvgIcon, { icon: 'mdi:devices', size: 16 }) },
  { key: 'history', label: t('nav.history'), icon: () => h(TSvgIcon, { icon: 'mdi:history', size: 16 }) },
  { key: 'linked', label: t('nav.linked'), icon: () => h(TSvgIcon, { icon: 'mdi:link-variant', size: 16 }) },
  { key: 'danger', label: t('nav.danger'), icon: () => h(TSvgIcon, { icon: 'mdi:alert-circle-outline', size: 16 }) },
])

// ---- Profile state --------------------------------------------------------
const profile = ref<UserDto | null>(null)
const detail = ref<UserDetailDto | null>(null)
const loadingProfile = ref(false)
const savingProfile = ref(false)

interface ProfileForm {
  nickname: string
  email: string
  phoneNumber: string
  gender: number
  birthday: string | null
  bio: string
  address: string
  website: string
}
const form = reactive<ProfileForm>({
  nickname: '',
  email: '',
  phoneNumber: '',
  gender: 0,
  birthday: null,
  bio: '',
  address: '',
  website: '',
})

const genderOptions = computed(() => [
  { label: t('profile.genderUnknown'), value: 0 },
  { label: t('profile.genderMale'), value: 1 },
  { label: t('profile.genderFemale'), value: 2 },
])

function applyProfileToForm(p: UserDto): void {
  form.nickname = p.nickname ?? ''
  form.email = p.email ?? ''
  form.phoneNumber = p.phoneNumber ?? ''
  form.gender = p.gender ?? 0
  form.birthday = typeof p.birthday === 'string' ? p.birthday : (p.birthday ? new Date(p.birthday).toISOString().slice(0, 10) : null)
  form.bio = p.bio ?? ''
  form.address = p.address ?? ''
  form.website = p.website ?? ''
}

async function loadProfile(): Promise<void> {
  loadingProfile.value = true
  try {
    const p = await bridge.me.getProfile()
    profile.value = p
    applyProfileToForm(p)
    // detail load is optional — most installs use the basic profile, but we
    // try anyway so future fields (avatar metadata, etc.) are available.
    try { detail.value = await bridge.me.getDetail() } catch { /* ignore */ }
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loadingProfile.value = false
  }
}

function resetProfileForm(): void {
  if (profile.value) applyProfileToForm(profile.value)
}

const avatarInitials = computed(() => {
  const name = profile.value?.nickname || profile.value?.userName || '?'
  return name.slice(0, 1).toUpperCase()
})

async function saveProfile(): Promise<void> {
  savingProfile.value = true
  try {
    // Email / phone deliberately omitted — they require the verify-code
    // round-trip and are mutated through `openChangeEmail/openChangePhone`
    // modals, NOT this PUT.
    const payload: UpdateUserDto = {
      nickname: form.nickname || null,
      gender: form.gender,
      birthday: form.birthday || null,
      bio: form.bio || null,
      address: form.address || null,
      website: form.website || null,
    }
    const updated = await bridge.me.updateProfile(payload)
    profile.value = updated
    applyProfileToForm(updated)
    message.success(t('profile.saved'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    savingProfile.value = false
  }
}

// ---- Password -------------------------------------------------------------
const pwForm = reactive({ currentPassword: '', newPassword: '', confirm: '' })
const changingPassword = ref(false)
const canChangePassword = computed(
  () =>
    pwForm.currentPassword.length > 0 &&
    pwForm.newPassword.length >= 6 &&
    pwForm.newPassword === pwForm.confirm,
)
async function submitChangePassword(): Promise<void> {
  if (!canChangePassword.value) {
    message.warning(t('security.password.mismatch'))
    return
  }
  changingPassword.value = true
  try {
    await bridge.me.changePassword({
      currentPassword: pwForm.currentPassword,
      newPassword: pwForm.newPassword,
    })
    pwForm.currentPassword = ''
    pwForm.newPassword = ''
    pwForm.confirm = ''
    message.success(t('security.password.success'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    changingPassword.value = false
  }
}

// ---- 2FA status -----------------------------------------------------------
const twoFactor = ref<TwoFactorStatusDto | null>(null)
const togglingTwoFactor = ref(false)
async function loadTwoFactor(): Promise<void> {
  try {
    twoFactor.value = await bridge.me.getTwoFactorStatus()
  } catch {
    /* keep null */
  }
}
async function disableTwoFactor(): Promise<void> {
  togglingTwoFactor.value = true
  try {
    await bridge.me.disableTwoFactor()
    message.success(t('security.twoFactor.disableSuccess'))
    await loadTwoFactor()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    togglingTwoFactor.value = false
  }
}
async function disableTotp(): Promise<void> {
  togglingTwoFactor.value = true
  try {
    await bridge.me.disableTotp()
    message.success(t('security.twoFactor.totpDisableSuccess'))
    await loadTwoFactor()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    togglingTwoFactor.value = false
  }
}

// ---- Change email / phone (two-step verify) ------------------------------
interface ChangeModalState {
  show: boolean
  kind: 'email' | 'phone'
  target: string // new email or new phone
  code: string
  password: string
  step: 'send' | 'confirm'
  sending: boolean
  confirming: boolean
}
const changeModal = reactive<ChangeModalState>({
  show: false,
  kind: 'email',
  target: '',
  code: '',
  password: '',
  step: 'send',
  sending: false,
  confirming: false,
})
function openChangeEmail(): void {
  Object.assign(changeModal, {
    show: true, kind: 'email', target: '', code: '', password: '',
    step: 'send', sending: false, confirming: false,
  })
}
function openChangePhone(): void {
  Object.assign(changeModal, {
    show: true, kind: 'phone', target: '', code: '', password: '',
    step: 'send', sending: false, confirming: false,
  })
}
const changeModalTitle = computed(() =>
  t(changeModal.kind === 'email' ? 'changeModal.titleEmail' : 'changeModal.titlePhone'),
)
async function sendChangeCode(): Promise<void> {
  if (!changeModal.target.trim()) {
    message.warning(t('changeModal.targetRequired'))
    return
  }
  changeModal.sending = true
  try {
    if (changeModal.kind === 'email') {
      await bridge.me.sendChangeEmailCode({ newEmail: changeModal.target.trim() } as never)
    } else {
      await bridge.me.sendChangePhoneCode({ newPhoneNumber: changeModal.target.trim() } as never)
    }
    changeModal.step = 'confirm'
    message.success(t('changeModal.codeSent'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    changeModal.sending = false
  }
}
async function confirmChange(): Promise<void> {
  if (!changeModal.code.trim()) {
    message.warning(t('changeModal.codeRequired'))
    return
  }
  changeModal.confirming = true
  try {
    if (changeModal.kind === 'email') {
      await bridge.me.confirmChangeEmail({
        newEmail: changeModal.target.trim(),
        verificationCode: changeModal.code.trim(),
      } as never)
    } else {
      await bridge.me.confirmChangePhone({
        newPhoneNumber: changeModal.target.trim(),
        verificationCode: changeModal.code.trim(),
      } as never)
    }
    message.success(t('changeModal.success'))
    changeModal.show = false
    await loadProfile()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    changeModal.confirming = false
  }
}

// ---- Sessions -------------------------------------------------------------
const sessions = ref<UserSessionDto[]>([])
const loadingSessions = ref(false)
async function loadSessions(): Promise<void> {
  loadingSessions.value = true
  try {
    sessions.value = await bridge.me.getSessions()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loadingSessions.value = false
  }
}
async function revokeOne(row: UserSessionDto): Promise<void> {
  try {
    await bridge.me.revokeSession(row.id)
    message.success(t('sessions.revoked'))
    await loadSessions()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}
async function revokeAll(): Promise<void> {
  try {
    await bridge.me.revokeAllSessions()
    message.success(t('sessions.allRevoked'))
    // Revoking ALL sessions necessarily kills the current one — bounce
    // to login instead of leaving a half-dead admin shell.
    logoutAndRedirect()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

const sessionColumns = computed<DataTableColumns<UserSessionDto>>(() => [
  {
    key: 'deviceInfo',
    title: t('sessions.cols.device'),
    render: (row) => {
      // Same icon+label rendering as the admin SessionManagement page.
      // Keeping the visual identical across "my sessions" and "user's
      // sessions" makes the two surfaces feel coherent.
      const profile = parseDeviceInfo(row.deviceInfo)
      return h(
        'span',
        { style: 'display: inline-flex; align-items: center; gap: 6px' },
        [
          h(TSvgIcon, {
            icon: profile.icon,
            size: 16,
            style: `color: ${deviceIconColor(profile.osFamily)}`,
          }),
          h(
            'span',
            { style: 'font-size: 13px', title: row.deviceInfo ?? '' },
            profile.label,
          ),
        ],
      )
    },
  },
  { key: 'ipAddress', title: t('sessions.cols.ip') },
  {
    key: 'lastActivityTime',
    title: t('sessions.cols.lastActive'),
    render: (row) => formatTime(row.lastActivityTime),
  },
  {
    key: 'actions',
    title: t('sessions.cols.actions'),
    width: 120,
    render: (row) =>
      h(
        NPopconfirm,
        {
          onPositiveClick: () => revokeOne(row),
        },
        {
          trigger: () =>
            h(
              NButton,
              { size: 'tiny', type: 'error', ghost: true },
              { default: () => t('sessions.revoke') },
            ),
          default: () => t('sessions.confirmRevoke'),
        },
      ),
  },
])

// ---- Login history --------------------------------------------------------
const history = ref<LoginLogDto[]>([])
const loadingHistory = ref(false)
async function loadHistory(): Promise<void> {
  loadingHistory.value = true
  try {
    history.value = await bridge.me.getLoginHistory()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loadingHistory.value = false
  }
}

const historyColumns = computed<DataTableColumns<LoginLogDto>>(() => [
  {
    key: 'loginTime',
    title: t('history.cols.time'),
    render: (row) => formatTime(row.loginTime),
  },
  { key: 'ipAddress', title: t('history.cols.ip') },
  { key: 'deviceInfo', title: t('history.cols.device') },
  {
    key: 'isSuccess',
    title: t('history.cols.result'),
    render: (row) =>
      h(
        NTag,
        { size: 'small', bordered: false, type: row.isSuccess ? 'success' : 'error' },
        { default: () => (row.isSuccess ? t('history.success') : t('history.failed')) },
      ),
  },
  { key: 'failureReason', title: t('history.cols.reason') },
])

// ---- Linked accounts ------------------------------------------------------
const linked = ref<UserLoginDto[]>([])
const loadingLinked = ref(false)
async function loadLinked(): Promise<void> {
  loadingLinked.value = true
  try {
    linked.value = await bridge.me.getLinkedAccounts()
  } catch {
    linked.value = []
  } finally {
    loadingLinked.value = false
  }
}
async function unlink(provider: string): Promise<void> {
  try {
    await bridge.me.unlinkAccount(provider)
    message.success(t('linked.unlinked'))
    await loadLinked()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// ---- Danger zone ----------------------------------------------------------
const exporting = ref(false)
const deactivating = ref(false)
const deleting = ref(false)

async function exportData(): Promise<void> {
  exporting.value = true
  try {
    const data = await bridge.me.exportPersonalData()
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `personal-data-${new Date().toISOString().slice(0, 10)}.json`
    a.click()
    URL.revokeObjectURL(url)
    message.success(t('danger.export.success'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    exporting.value = false
  }
}

async function deactivateAccount(): Promise<void> {
  deactivating.value = true
  try {
    await bridge.me.deactivate()
    message.success(t('danger.deactivate.success'))
    // Account is now disabled — current session is dead from the server's
    // POV. Avoid the "next API call 401s into a blank screen" state.
    logoutAndRedirect()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    deactivating.value = false
  }
}

async function deleteAccount(): Promise<void> {
  deleting.value = true
  try {
    await bridge.me.deleteAccount()
    message.success(t('danger.delete.success'))
    logoutAndRedirect()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    deleting.value = false
  }
}

// ---- Helpers + lifecycle --------------------------------------------------
function formatTime(v?: string | Date | null): string {
  if (!v) return '—'
  try {
    const d = new Date(v)
    const y = d.getFullYear()
    const M = String(d.getMonth() + 1).padStart(2, '0')
    const D = String(d.getDate()).padStart(2, '0')
    const hh = String(d.getHours()).padStart(2, '0')
    const mm = String(d.getMinutes()).padStart(2, '0')
    return `${y}-${M}-${D} ${hh}:${mm}`
  } catch {
    return ''
  }
}

async function reloadAll(): Promise<void> {
  await Promise.all([
    loadProfile(),
    loadTwoFactor(),
    loadSessions(),
    loadHistory(),
    loadLinked(),
  ])
}

onMounted(() => {
  void reloadAll()
})
</script>

<style scoped>
/* Root uses shared `.t-stack-page` (height:100% + flex column) so the
   single wrapping NCard can claim residual height and the right-pane
   sections control their own scroll. Modals teleport to body so they
   don't collide with these selectors; there's only one NCard inside
   `.t-user-center` itself. */
.t-user-center :deep(.n-card) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-user-center :deep(.n-card-content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.t-user-center__layout {
  display: grid;
  grid-template-columns: 200px 1fr;
  gap: 24px;
  flex: 1 1 auto;
  min-height: 0;
}
@media (max-width: 768px) {
  .t-user-center__layout {
    grid-template-columns: 1fr;
  }
}
.t-user-center__nav {
  border-right: 1px solid var(--tnzi-border);
  padding-right: 8px;
  overflow-y: auto;
  min-height: 0;
}
@media (max-width: 768px) {
  .t-user-center__nav {
    border-right: none;
    border-bottom: 1px solid var(--tnzi-border);
    padding-right: 0;
    padding-bottom: 8px;
    overflow-y: visible;
  }
}
/* Right pane is a flex column. Each section decides whether to scroll
   itself (--scroll: form / security / linked / danger) or let its
   NDataTable handle scroll via flex-height (--table: sessions / history).
   No overflow at this level so the table's flex-height has a definite
   parent box to size against. */
.t-user-center__content {
  min-width: 0;
  min-height: 0;
  padding: 0 4px;
  display: flex;
  flex-direction: column;
}
.t-user-center__section {
  display: flex;
  flex-direction: column;
  gap: 12px;
  flex: 1 1 auto;
  min-height: 0;
}
/* Form-heavy sections: scroll the section internally so deep forms
   stay reachable without the whole page scrolling. */
.t-user-center__section--scroll {
  overflow-y: auto;
}
/* Table sections: NDataTable inside grows to fill residual height —
   the table's built-in scroll body + pagination footer then handle
   internal vertical overflow. */
.t-user-center__section--table :deep(.n-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}
/* The NSpin wrapper inside the right pane is the only intermediate
   node between `.t-user-center__content` and the active section. It
   has to inherit flex/fill or the section can't claim the residual
   height for `:flex-height` on the NDataTable to work. */
.t-user-center__content :deep(.n-spin-container),
.t-user-center__content :deep(.n-spin-content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
.t-user-center__section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.t-user-center__section-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-user-center__identity {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 0 4px;
}
.t-user-center__avatar {
  width: 64px;
  height: 64px;
  border-radius: 50%;
  background: rgb(var(--tnzi-primary-rgb) / 0.12);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  flex-shrink: 0;
}
.t-user-center__avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.t-user-center__avatar-initials {
  color: var(--tnzi-primary);
  font-size: 28px;
  font-weight: 600;
  line-height: 1;
}
.t-user-center__user-name {
  margin: 0 0 4px;
  font-size: 18px;
  font-weight: 600;
}
.t-user-center__user-meta {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
.t-user-center__hint {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
  margin: 4px 0;
}
.t-user-center__form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px 16px;
  margin-bottom: 12px;
}
@media (max-width: 640px) {
  .t-user-center__form-grid {
    grid-template-columns: 1fr;
  }
}
/*
 * `:show-feedback="false"` strips the 24px feedback slot that normally
 * spaces consecutive NFormItems, so single-column form-items (bio /
 * address / website / change-password fields when stacked) collide
 * vertically. Restore explicit spacing. Inside the grid, the grid gap
 * already handles spacing — reset the per-item margin there to avoid
 * doubled gaps.
 */
.t-user-center__section :deep(.n-form-item) {
  margin-bottom: 12px;
}
.t-user-center__section :deep(.n-form-item .n-form-item-label) {
  padding-bottom: 4px;
}
.t-user-center__form-grid :deep(.n-form-item) {
  margin-bottom: 0;
}
.t-user-center__actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}
.t-user-center__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 10px 0;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-user-center__row:last-child {
  border-bottom: none;
}
.t-user-center__row-label {
  font-weight: 500;
  font-size: 13px;
}
.t-user-center__linked-list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.t-user-center__linked-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 0;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-user-center__linked-item:last-child {
  border-bottom: none;
}
.t-user-center__empty {
  padding: 24px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
  font-size: 13px;
}
</style>
