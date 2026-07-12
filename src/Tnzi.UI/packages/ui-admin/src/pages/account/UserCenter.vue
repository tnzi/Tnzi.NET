<template>
  <!--
    UserCenter — self-service personal center.

    BotDetail-style layout: a slim header (avatar + name + roles + refresh)
    over a left vertical menu (sections) + right content panel, both white
    cards. Built on the shared `TDetailLayout` (layout="side"). Each section
    is rendered inline so consumers don't register five sub-routes.
  -->
  <div class="t-user-center">
    <TDetailHost
      :state="pageDetail"
      layout="side"
      :sections="sections"
      :back="false"
      :translate="t"
    >
      <!-- Slim header: avatar + name + roles -->
      <template #title>
        <div class="t-user-center__head">
          <TAvatar
            :src="resolvedAvatarUrl"
            :name="profile?.nickname || profile?.userName || t('title')"
            :size="36"
            color="rgb(var(--tnzi-primary-rgb) / 0.12)"
            text-color="var(--tnzi-primary)"
          />
          <div class="t-user-center__head-text">
            <span class="t-user-center__head-name">{{ profile?.nickname || profile?.userName || t('title') }}</span>
            <span class="t-user-center__head-meta">
              <NTag v-for="r in profile?.roles ?? []" :key="r" size="tiny" :bordered="false">{{ r }}</NTag>
            </span>
          </div>
        </div>
      </template>

      <template #actions>
        <NButton size="small" tertiary :loading="loadingProfile" @click="reloadAll">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('refresh') }}
        </NButton>
      </template>

      <template #default>
        <div class="t-user-center__panel">
          <NSpin :show="loadingProfile && !profile">
            <!-- ===== Basic profile ===== -->
            <section v-if="activeTab === 'profile'" class="t-user-center__section">
              <header class="t-user-center__section-bar">
                <h3 class="t-user-center__section-title">{{ t('nav.profile') }}</h3>
              </header>
              <div class="t-user-center__section-body">
                <div class="t-detail-content">
                <!-- Avatar upload pushes to the Storage module — hide the
                     picker on hosts that never loaded it (v-module is the
                     module twin of v-permission; imported locally so bare
                     test mounts resolve it without global registration). -->
                <div v-module="'storage'" class="t-user-center__avatar-field">
                  <TImageUpload
                    shape="circle"
                    :cropper="true"
                    :model-value="resolvedAvatarUrl"
                    :disabled="savingAvatar"
                    :upload="handleAvatarUpload"
                    @error="(msg: string) => message.error(msg)"
                  />
                  <div class="t-user-center__avatar-field-text">
                    <div class="t-user-center__avatar-field-label">{{ t('profile.avatar') }}</div>
                    <div class="t-user-center__hint">{{ t('profile.avatarHint') }}</div>
                  </div>
                </div>
                <NForm label-placement="top" size="small" :show-feedback="false">
                  <div class="t-user-center__form-grid">
                    <NFormItem :label="t('profile.userName')">
                      <NInput :value="profile?.userName ?? ''" disabled />
                    </NFormItem>
                    <NFormItem :label="t('profile.nickname')">
                      <NInput v-model:value="form.nickname" :placeholder="t('profile.nicknamePlaceholder')" />
                    </NFormItem>
                    <NFormItem :label="t('profile.firstName')">
                      <NInput v-model:value="form.firstName" :placeholder="t('profile.firstNamePlaceholder')" />
                    </NFormItem>
                    <NFormItem :label="t('profile.lastName')">
                      <NInput v-model:value="form.lastName" :placeholder="t('profile.lastNamePlaceholder')" />
                    </NFormItem>
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
                        class="w-full"
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
                <div class="t-user-center__save-bar">
                  <NButton size="small" @click="resetProfileForm">{{ t('reset') }}</NButton>
                  <NButton size="small" type="primary" :loading="savingProfile" @click="saveProfile">
                    {{ t('save') }}
                  </NButton>
                </div>
                </div>
              </div>
            </section>

            <!-- ===== Security ===== -->
            <section v-else-if="activeTab === 'security'" class="t-user-center__section">
              <header class="t-user-center__section-bar">
                <h3 class="t-user-center__section-title">{{ t('nav.security') }}</h3>
              </header>
              <div class="t-user-center__section-body">
                <div class="t-detail-content">
                <h4 class="t-user-center__sub-title">{{ t('security.password.title') }}</h4>
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
                    size="small"
                    :loading="changingPassword"
                    :disabled="!canChangePassword"
                    @click="submitChangePassword"
                  >
                    {{ t('security.password.submit') }}
                  </NButton>
                </div>

                <NDivider />

                <h4 class="t-user-center__sub-title">{{ t('security.twoFactor.title') }}</h4>
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
              </div>
            </section>

            <!-- ===== Sessions (login devices) ===== -->
            <section v-else-if="activeTab === 'sessions'" class="t-user-center__section">
              <header class="t-user-center__section-bar">
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
              <div class="t-user-center__section-body t-user-center__section-body--fill">
                <p class="t-user-center__hint">{{ t('sessions.hint') }}</p>
                <TResponsiveTable
                  class="t-user-center__table"
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
            </section>

            <!-- ===== Login history ===== -->
            <section v-else-if="activeTab === 'history'" class="t-user-center__section">
              <header class="t-user-center__section-bar">
                <h3 class="t-user-center__section-title">{{ t('history.title') }}</h3>
                <NButton size="small" tertiary :loading="loadingHistory" @click="loadHistory">
                  <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
                  {{ t('refresh') }}
                </NButton>
              </header>
              <div class="t-user-center__section-body t-user-center__section-body--fill">
                <TResponsiveTable
                  class="t-user-center__table"
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
            </section>

            <!-- ===== Linked accounts ===== -->
            <section v-else-if="activeTab === 'linked'" class="t-user-center__section">
              <header class="t-user-center__section-bar">
                <h3 class="t-user-center__section-title">{{ t('linked.title') }}</h3>
              </header>
              <div class="t-user-center__section-body">
                <div class="t-detail-content">
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
              </div>
            </section>

            <!-- ===== Danger zone ===== -->
            <section v-else-if="activeTab === 'danger'" class="t-user-center__section">
              <header class="t-user-center__section-bar">
                <h3 class="t-user-center__section-title">{{ t('danger.title') }}</h3>
              </header>
              <div class="t-user-center__section-body">
                <div class="t-detail-content">
                <p class="t-user-center__hint">{{ t('danger.hint') }}</p>

                <NAlert type="info" :show-icon="false">
                  <strong>{{ t('danger.export.title') }}</strong>
                  <p class="t-user-center__hint">{{ t('danger.export.hint') }}</p>
                  <NButton size="small" :loading="exporting" @click="exportData">
                    <template #icon><TSvgIcon icon="mdi:download" :size="14" /></template>
                    {{ t('danger.export.button') }}
                  </NButton>
                </NAlert>

                <NAlert type="warning" :show-icon="false">
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
              </div>
            </section>
          </NSpin>
        </div>
      </template>
    </TDetailHost>

    <!-- Change email / phone (two-step verify) -->
    <NModal v-model:show="changeModal.show" preset="card" size="small" class="w-480px">
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
        <div class="flex justify-end gap-8px">
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
import { computed, h, onMounted, reactive, ref, type Ref } from 'vue'
import { useRouter } from 'vue-router'
import type { DataTableColumns } from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import {
  NAlert,
  NButton,
  NDatePicker,
  NDivider,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSpin,
  NTag,
} from 'naive-ui'
import { TSvgIcon, TImageUpload, TAvatar } from '@tnzi/ui'
import { vModule } from '../../directives/vModule'
import { formatDateTime } from '@tnzi/core'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useDetail, type DetailSection } from '../../headless/useDetail'
import { useSafeMessage } from '../_shared/safeMessage'
import { deviceIconColor, parseDeviceInfo } from '../_shared/device-info'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { resolveAvatarUrl } from '../../utils/resolveAvatarUrl'
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

const client = useAdminClient()
const bridge = createIdentityBridge({ client })
const storageBridge = createStorageBridge({ client })
// Avatar rendering only needs the (synchronous) preview-URL builder.
const avatarStorage = { getPreviewUrl: storageBridge.files.previewUrl }
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

// ---- Guarded loading ------------------------------------------------------
/**
 * Every read-only load goes through this guard so a loading flag can NEVER
 * be stranded (the "spinner keeps rotating forever" class of bug):
 *
 * - `try/finally` always releases the flag — even when `apply` throws on a
 *   malformed/failed-envelope payload;
 * - a generation token makes the **latest** call the sole owner of the flag
 *   and the data, so concurrent invocations (mount + Refresh clicks) can
 *   neither clear the spinner early nor overwrite fresh results with stale
 *   late-resolving ones;
 * - a timeout race ends the wait when the HTTP layer never settles (hung
 *   connection / token-refresh deadlock — the request promise itself may
 *   stay pending forever, which no try/finally can recover from), degrading
 *   the infinite spinner into an error toast + a retryable Refresh button.
 */
const LOAD_TIMEOUT_MS = 15_000

function createGuardedLoader<T>(options: {
  flag: Ref<boolean>
  fetch: () => Promise<T>
  apply: (result: T) => void
  onError: (e: unknown) => void
}): () => Promise<void> {
  let generation = 0
  return async () => {
    const token = ++generation
    options.flag.value = true
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      const timeout = new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error(t('loadTimeout'))), LOAD_TIMEOUT_MS)
      })
      const result = await Promise.race([options.fetch(), timeout])
      if (token === generation) options.apply(result)
    } catch (e) {
      if (token === generation) options.onError(e)
    } finally {
      if (timer !== undefined) clearTimeout(timer)
      if (token === generation) options.flag.value = false
    }
  }
}

// ---- Active section -------------------------------------------------------
// Sections are grouped into three buckets in the left menu (BotDetail-style
// grouped NMenu): Account (profile/security), Activity (sessions/history),
// Advanced (linked/danger). `group` is the pre-translated label; TDetailLayout
// renders one NMenu group header per distinct value, preserving first-seen order.
const sections = computed<DetailSection[]>(() => [
  { key: 'profile', label: t('nav.profile'), icon: 'mdi:account-outline', group: t('nav.groups.account') },
  { key: 'security', label: t('nav.security'), icon: 'mdi:shield-lock-outline', group: t('nav.groups.account') },
  { key: 'sessions', label: t('nav.sessions'), icon: 'mdi:devices', group: t('nav.groups.activity') },
  { key: 'history', label: t('nav.history'), icon: 'mdi:history', group: t('nav.groups.activity') },
  { key: 'linked', label: t('nav.linked'), icon: 'mdi:link-variant', group: t('nav.groups.advanced') },
  { key: 'danger', label: t('nav.danger'), icon: 'mdi:alert-circle-outline', group: t('nav.groups.advanced') },
])

// Active section is two-way bound to `?section=` (deep-linkable + the browser
// Back/Forward buttons step through sections) via the shared composable;
// defaults to the Profile section.
const pageDetail = useDetail({
  mode: 'page',
  sectionUrl: true,
  sections,
  defaultSection: 'profile',
})
const activeTab = pageDetail.activeSection

// ---- Profile state --------------------------------------------------------
const profile = ref<UserDto | null>(null)
const detail = ref<UserDetailDto | null>(null)
const loadingProfile = ref(false)
const savingProfile = ref(false)

interface ProfileForm {
  firstName: string
  lastName: string
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
  firstName: '',
  lastName: '',
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
  form.firstName = p.firstName ?? ''
  form.lastName = p.lastName ?? ''
  form.nickname = p.nickname ?? ''
  form.email = p.email ?? ''
  form.phoneNumber = p.phoneNumber ?? ''
  form.gender = p.gender ?? 0
  form.birthday = typeof p.birthday === 'string' ? p.birthday : (p.birthday ? new Date(p.birthday).toISOString().slice(0, 10) : null)
  form.bio = p.bio ?? ''
  form.address = p.address ?? ''
  form.website = p.website ?? ''
}

const loadProfile = createGuardedLoader<UserDto>({
  flag: loadingProfile,
  fetch: () => bridge.me.getProfile(),
  apply: (p) => {
    // A failed backend envelope unwraps to `undefined` — surface it as a
    // real error (caught by the guard) instead of a TypeError + half form.
    if (!p) throw new Error(t('loadFailed'))
    profile.value = p
    applyProfileToForm(p)
    // detail load is optional — most installs use the basic profile, but we
    // try anyway so future fields (avatar metadata, etc.) are available.
    // Fire-and-forget OUTSIDE the loadingProfile window so a slow or hung
    // detail endpoint can never pin the profile spinner.
    void bridge.me
      .getDetail()
      .then((d) => { detail.value = d })
      .catch(() => undefined)
  },
  onError: (e) => message.error(e instanceof Error ? e.message : String(e)),
})

function resetProfileForm(): void {
  if (profile.value) applyProfileToForm(profile.value)
}

// Resolved avatar URL for the header `TAvatar` + the upload widget's preview.
// Reads the (possibly newer) `detail` first since the detail endpoint owns
// `avatarUrl`, then falls back to the basic `profile` (`avatar`/`avatarId`).
// `TAvatar` handles the broken-image → name-initial degradation internally.
const resolvedAvatarUrl = computed<string | null>(
  () => resolveAvatarUrl(detail.value, avatarStorage) ?? resolveAvatarUrl(profile.value, avatarStorage),
)

const savingAvatar = ref(false)

/**
 * `TImageUpload`'s `upload` handler: push the cropped blob to the storage
 * module, then hand back the file id + an anonymous preview URL so the widget
 * can render it immediately. The id is persisted to the user's profile via the
 * `avatarId` field of `PUT /users/profile/detail`.
 */
async function handleAvatarUpload(file: File | Blob): Promise<{ id?: string; url: string }> {
  savingAvatar.value = true
  try {
    // `TImageUpload` may emit a cropped Blob; the storage API takes a File, so
    // wrap it with a sensible filename when needed.
    const toUpload =
      file instanceof File ? file : new File([file], 'avatar.png', { type: file.type || 'image/png' })
    // The storage bridge pre-unwraps the ApiResult envelope → FileUploadResultDto.
    const uploaded = await storageBridge.files.upload(toUpload)
    const id = uploaded?.id
    if (!id) throw new Error(t('profile.avatarUploadFailed'))
    const url = storageBridge.files.previewUrl(id)

    // Persist the new avatar id. CRITICAL: the backend detail update is
    // REPLACE-semantics (Mapster maps every field, nulls included), so a
    // partial `{ avatarId }` would wipe nickname/gender/bio. Send the full
    // current form payload alongside the new avatar id.
    const updated = await bridge.me.updateProfile({
      firstName: form.firstName || null,
      lastName: form.lastName || null,
      nickname: form.nickname || null,
      gender: form.gender,
      birthday: form.birthday || null,
      bio: form.bio || null,
      address: form.address || null,
      website: form.website || null,
      avatarId: id,
      avatarUrl: null,
    } as UpdateUserDto)
    if (updated) {
      profile.value = updated
      applyProfileToForm(updated)
    } else if (profile.value) {
      profile.value = { ...profile.value, avatarId: id, avatar: null }
    }
    // Reflect the new id on `detail` too (it drives `avatarUrl`'s first branch).
    detail.value = detail.value
      ? { ...detail.value, avatarId: id, avatarUrl: null }
      : detail.value
    // Mirror onto the auth store so the header-bar avatar (outside this page)
    // refreshes without a reload.
    if (authStore.userInfo) {
      authStore.setUserInfo({ ...authStore.userInfo, avatar: url })
    }
    message.success(t('profile.avatarUpdated'))
    return { id, url }
  } finally {
    savingAvatar.value = false
  }
}

async function saveProfile(): Promise<void> {
  savingProfile.value = true
  try {
    // Email / phone deliberately omitted — they require the verify-code
    // round-trip and are mutated through `openChangeEmail/openChangePhone`
    // modals, NOT this PUT.
    // avatarId/avatarUrl MUST be carried through: the backend detail update is
    // REPLACE-semantics, so omitting them would wipe a previously-uploaded
    // avatar the moment the user saves any other profile field (this was the
    // "avatar disappears after refresh" bug — a Save silently nulled avatarId).
    const payload: UpdateUserDto = {
      firstName: form.firstName || null,
      lastName: form.lastName || null,
      nickname: form.nickname || null,
      gender: form.gender,
      birthday: form.birthday || null,
      bio: form.bio || null,
      address: form.address || null,
      website: form.website || null,
      avatarId: detail.value?.avatarId ?? profile.value?.avatarId ?? null,
      avatarUrl: detail.value?.avatarUrl ?? null,
    }
    const updated = await bridge.me.updateProfile(payload)
    profile.value = updated
    applyProfileToForm(updated)
    // Mirror the new display name onto the auth store so the header bar (outside
    // this page) updates live — same precedence as the backend: nickname → real
    // name → username.
    if (authStore.userInfo && updated) {
      const fullName = [updated.firstName, updated.lastName].filter(Boolean).join(' ').trim()
      authStore.setUserInfo({
        ...authStore.userInfo,
        displayName: updated.nickname || (fullName || undefined) || authStore.userInfo.username,
        // Short label (header bar / greeting / chat "me") — first name, no surname.
        shortName: updated.nickname || updated.firstName || authStore.userInfo.username,
      })
    }
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
    // Step 1 sends the code to the NEW address. Backend
    // `SendChangeVerificationCodeDto` carries a single `newAddress` field for
    // both email and phone (it is the destination the code is sent to).
    await (changeModal.kind === 'email'
      ? bridge.me.sendChangeEmailCode({ newAddress: changeModal.target.trim() })
      : bridge.me.sendChangePhoneCode({ newAddress: changeModal.target.trim() }))
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
    // Step 2 confirms with the new address + the emailed/texted code. Backend
    // DTOs are `ChangeEmailDto { newEmail, code }` /
    // `ChangePhoneNumberDto { newPhoneNumber, code }` — the verification field
    // is `code`, not `verificationCode`.
    if (changeModal.kind === 'email') {
      await bridge.me.confirmChangeEmail({
        newEmail: changeModal.target.trim(),
        code: changeModal.code.trim(),
      })
    } else {
      await bridge.me.confirmChangePhone({
        newPhoneNumber: changeModal.target.trim(),
        code: changeModal.code.trim(),
      })
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
const loadSessions = createGuardedLoader<UserSessionDto[]>({
  flag: loadingSessions,
  fetch: () => bridge.me.getSessions(),
  apply: (rows) => { sessions.value = rows ?? [] },
  onError: (e) => message.error(e instanceof Error ? e.message : String(e)),
})
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
      const deviceProfile = parseDeviceInfo(row.deviceInfo)
      return h(
        'span',
        { class: 'inline-flex items-center gap-6px' },
        [
          h(TSvgIcon, {
            icon: deviceProfile.icon,
            size: 16,
            style: `color: ${deviceIconColor(deviceProfile.osFamily)}`,
          }),
          h(
            'span',
            { class: 'text-13px', title: row.deviceInfo ?? '' },
            deviceProfile.label,
          ),
        ],
      )
    },
  },
  { key: 'ipAddress', title: t('sessions.cols.ip') },
  {
    key: 'lastActivityTime',
    title: t('sessions.cols.lastActive'),
    render: (row) => formatDateTime(row.lastActivityTime, { fallback: '—' }),
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
const loadHistory = createGuardedLoader<LoginLogDto[]>({
  flag: loadingHistory,
  fetch: () => bridge.me.getLoginHistory(),
  apply: (rows) => { history.value = rows ?? [] },
  onError: (e) => message.error(e instanceof Error ? e.message : String(e)),
})

const historyColumns = computed<DataTableColumns<LoginLogDto>>(() => [
  {
    key: 'loginTime',
    title: t('history.cols.time'),
    render: (row) => formatDateTime(row.loginTime, { fallback: '—' }),
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
const loadLinked = createGuardedLoader<UserLoginDto[]>({
  flag: loadingLinked,
  fetch: () => bridge.me.getLinkedAccounts(),
  apply: (rows) => { linked.value = rows ?? [] },
  // Linked accounts are optional (external providers may be disabled) —
  // keep the original fail-silent behaviour, just with a guaranteed reset.
  onError: () => { linked.value = [] },
})
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
.t-user-center {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* Slim header (avatar + name + roles) rendered in TDetailLayout's #title. */
.t-user-center__head {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}
.t-user-center__head-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.t-user-center__head-name {
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  line-height: 1.2;
}
.t-user-center__head-meta {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

/* Right content panel — fills TDetailLayout's white panel card. The panel
   itself never scrolls; each section owns a fixed bar + a scrolling (or
   flex-height-filling) body, mirroring BotDetail's per-tab header + body. */
.t-user-center__panel {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-user-center__panel :deep(.n-spin-container),
.t-user-center__panel :deep(.n-spin-content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}

/* A section = a fixed header bar + a body that claims the residual height. */
.t-user-center__section {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}

/* Fixed header bar: title (left) + section actions (right). Stays pinned
   while the body scrolls underneath. */
.t-user-center__section-bar {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 52px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--tnzi-border);
}

/* Body: fills the remaining height. Forms / lists scroll here; table sections
   add `--fill` so the flex-height NDataTable owns the scroll instead. */
.t-user-center__section-body {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-user-center__section-body--fill {
  overflow: hidden;
}
.t-user-center__table {
  flex: 1 1 auto;
  min-height: 0;
}

/* Profile-section avatar uploader: the circular picker + a label/hint column. */
.t-user-center__avatar-field {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 16px;
}
.t-user-center__avatar-field-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.t-user-center__avatar-field-label {
  font-weight: 500;
  font-size: 13px;
  color: var(--tnzi-base-text);
}

.t-user-center__section-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
/* In-body sub-heading (password / 2FA blocks within the Security section). */
.t-user-center__sub-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
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
/* Form-end action bar (Save / Reset) — right-aligned within the capped
   content column so the buttons line up with the form's right edge, never
   floating out at the full panel width. Mirrors BotDetail's `.save-bar`. */
.t-user-center__save-bar {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
  padding-top: 14px;
  border-top: 1px solid var(--tnzi-border);
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
