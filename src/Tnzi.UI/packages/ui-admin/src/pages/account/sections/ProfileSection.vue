<template>
  <TUserCenterSection :title="t('nav.profile')">
    <!-- Avatar upload pushes to the Storage module - hide the picker on hosts
         that never loaded it (v-module is the module twin of v-permission). -->
    <div v-module="'storage'" class="t-uc-avatar-field">
      <TImageUpload
        shape="circle"
        :cropper="true"
        :model-value="ctx.resolvedAvatarUrl.value"
        :disabled="savingAvatar"
        :upload="handleAvatarUpload"
        :title="t('profile.avatarHint')"
        removable
        :remove-label="t('profile.avatarRemove')"
        @remove="handleAvatarRemove"
        @error="(msg: string) => ctx.message.error(msg)"
      />
      <div class="t-uc-avatar-field-text">
        <div class="t-uc-avatar-field-label">{{ t('profile.avatar') }}</div>
      </div>
    </div>

    <NForm label-placement="top" :show-feedback="false">
      <div class="t-uc-form-grid">
        <NFormItem :label="t('profile.userName')">
          <NInput :value="ctx.profile.value?.userName ?? ''" disabled />
        </NFormItem>
        <NFormItem v-if="showField('nickname')" :label="t('profile.nickname')">
          <NInput
            v-model:value="form.nickname"
            :disabled="readonly('nickname')"
            :placeholder="t('profile.nicknamePlaceholder')"
          />
        </NFormItem>
        <NFormItem v-if="showField('firstName')" :label="t('profile.firstName')">
          <NInput
            v-model:value="form.firstName"
            :disabled="readonly('firstName')"
            :placeholder="t('profile.firstNamePlaceholder')"
          />
        </NFormItem>
        <NFormItem v-if="showField('lastName')" :label="t('profile.lastName')">
          <NInput
            v-model:value="form.lastName"
            :disabled="readonly('lastName')"
            :placeholder="t('profile.lastNamePlaceholder')"
          />
        </NFormItem>
        <NFormItem :label="t('profile.email')">
          <NInput :value="ctx.profile.value?.email ?? ''" disabled :placeholder="t('profile.emailPlaceholder')">
            <template v-if="ctx.capabilities.value.emailChannel" #suffix>
              <NButton size="tiny" tertiary @click="openChangeEmail">{{ t('profile.change') }}</NButton>
            </template>
          </NInput>
        </NFormItem>
        <NFormItem :label="t('profile.phone')">
          <NInput
            :value="ctx.profile.value?.phoneNumber ?? ''"
            disabled
            :placeholder="t('profile.phonePlaceholder')"
          >
            <template v-if="ctx.capabilities.value.smsChannel" #suffix>
              <NButton size="tiny" tertiary @click="openChangePhone">{{ t('profile.change') }}</NButton>
            </template>
          </NInput>
        </NFormItem>
        <NFormItem v-if="showField('gender')" :label="t('profile.gender')">
          <NSelect
            v-model:value="form.gender"
            :options="genderOptions"
            :disabled="readonly('gender')"
            :placeholder="t('profile.genderPlaceholder')"
          />
        </NFormItem>
        <NFormItem v-if="showField('birthday')" :label="t('profile.birthday')">
          <NDatePicker
            v-model:formatted-value="form.birthday"
            value-format="yyyy-MM-dd"
            type="date"
            clearable
            :disabled="readonly('birthday')"
            class="w-full"
          />
        </NFormItem>
      </div>
      <NFormItem v-if="showField('bio')" :label="t('profile.bio')">
        <NInput
          v-model:value="form.bio"
          type="textarea"
          :rows="3"
          :disabled="readonly('bio')"
          :placeholder="t('profile.bioPlaceholder')"
        />
      </NFormItem>
      <NFormItem v-if="showField('address')" :label="t('profile.address')">
        <NInput
          v-model:value="form.address"
          :disabled="readonly('address')"
          :placeholder="t('profile.addressPlaceholder')"
        />
      </NFormItem>
      <NFormItem v-if="showField('website')" :label="t('profile.website')">
        <NInput
          v-model:value="form.website"
          :disabled="readonly('website')"
          :placeholder="t('profile.websitePlaceholder')"
        />
      </NFormItem>
    </NForm>

    <div class="t-uc-save-bar">
      <NButton size="small" @click="resetForm">{{ t('reset') }}</NButton>
      <NButton size="small" type="primary" :loading="saving" @click="save">{{ t('save') }}</NButton>
    </div>

    <!-- Change email / phone (two-step verify). TModalShell = shared modal
         chrome (width cap + auto-fullscreen on phones + no-autofocus on phones). -->
    <TModalShell v-model:show="changeModal.show" :title="changeModalTitle" :width="480">
      <NForm label-placement="top" :show-feedback="false">
        <NFormItem
          :label="t(changeModal.kind === 'email' ? 'changeModal.newEmail' : 'changeModal.newPhone')"
          required
        >
          <NInput
            v-model:value="changeModal.target"
            :disabled="changeModal.step === 'confirm'"
            :placeholder="t(changeModal.kind === 'email' ? 'changeModal.newEmailPlaceholder' : 'changeModal.newPhonePlaceholder')"
          />
        </NFormItem>
        <NFormItem v-if="changeModal.step === 'confirm'" :label="t('changeModal.code')" required>
          <NInput v-model:value="changeModal.code" :placeholder="t('changeModal.codePlaceholder')" />
        </NFormItem>
        <p class="t-uc-hint">{{ t('changeModal.hint') }}</p>
      </NForm>
      <template #footer>
        <NButton size="small" @click="changeModal.show = false">{{ t('changeModal.cancel') }}</NButton>
        <NButton
          v-if="changeModal.step === 'send'"
          size="small"
          type="primary"
          :loading="changeModal.sending"
          :disabled="!changeModal.target.trim()"
          @click="sendChangeCode"
        >
          {{ t('changeModal.sendCode') }}
        </NButton>
        <NButton
          v-else
          size="small"
          type="primary"
          :loading="changeModal.confirming"
          :disabled="!changeModal.code.trim()"
          @click="confirmChange"
        >
          {{ t('changeModal.confirm') }}
        </NButton>
      </template>
    </TModalShell>
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NDatePicker, NForm, NFormItem, NInput, NSelect } from 'naive-ui'
import { TImageUpload } from '@tnzi/ui'
import type { UserDto, UpdateUserDto } from '@tnzi/core/services/identity'
import TUserCenterSection from './TUserCenterSection.vue'
import TModalShell from '../../../components/overlay/TModalShell.vue'
import { vModule } from '../../../directives/vModule'
import { useUserCenterContext } from '../userCenterContext'
import type { UserCenterProfileField } from '../../../plugin/userCenterConfig'

const ctx = useUserCenterContext()
const t = ctx.t

// ── Profile form ──
interface ProfileForm {
  firstName: string
  lastName: string
  nickname: string
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
  gender: 0,
  birthday: null,
  bio: '',
  address: '',
  website: '',
})
const saving = ref(false)
const savingAvatar = ref(false)

const showField = (f: UserCenterProfileField) => !ctx.isFieldHidden(f)
const readonly = (f: UserCenterProfileField) => ctx.isFieldReadonly(f)

const genderOptions = computed(() => [
  { label: t('profile.genderUnknown'), value: 0 },
  { label: t('profile.genderMale'), value: 1 },
  { label: t('profile.genderFemale'), value: 2 },
])

function applyProfileToForm(p: UserDto): void {
  form.firstName = p.firstName ?? ''
  form.lastName = p.lastName ?? ''
  form.nickname = p.nickname ?? ''
  form.gender = p.gender ?? 0
  form.birthday =
    typeof p.birthday === 'string'
      ? p.birthday
      : p.birthday
        ? new Date(p.birthday).toISOString().slice(0, 10)
        : null
  form.bio = p.bio ?? ''
  form.address = p.address ?? ''
  form.website = p.website ?? ''
}

// Fill (and re-fill) the form whenever the shell's shared profile changes - the
// shell owns loading; this section is a pure view/editor over `ctx.profile`.
watch(
  () => ctx.profile.value,
  (p) => {
    if (p) applyProfileToForm(p)
  },
  { immediate: true },
)

function resetForm(): void {
  if (ctx.profile.value) applyProfileToForm(ctx.profile.value)
}

/** Full REPLACE-semantics payload: the backend detail update maps every field
 *  (nulls included), so a partial payload would wipe untouched fields (avatar,
 *  nickname, …). Always send the full current form + the carried avatar id. */
function buildPayload(avatarId: string | null, avatarUrl: string | null): UpdateUserDto {
  return {
    firstName: form.firstName || null,
    lastName: form.lastName || null,
    nickname: form.nickname || null,
    gender: form.gender,
    birthday: form.birthday || null,
    bio: form.bio || null,
    address: form.address || null,
    website: form.website || null,
    avatarId,
    avatarUrl,
  } as UpdateUserDto
}

function mirrorDisplayName(updated: UserDto): void {
  if (!ctx.authStore.userInfo) return
  const fullName = [updated.firstName, updated.lastName].filter(Boolean).join(' ').trim()
  ctx.authStore.setUserInfo({
    ...ctx.authStore.userInfo,
    displayName: updated.nickname || fullName || ctx.authStore.userInfo.username,
    // Short label (header bar / greeting / chat "me") - first name, no surname.
    shortName: updated.nickname || updated.firstName || ctx.authStore.userInfo.username,
  })
}

async function save(): Promise<void> {
  saving.value = true
  try {
    // Email / phone deliberately omitted - mutated through the verify-code modal.
    // avatarId/avatarUrl MUST be carried through (REPLACE-semantics) so saving a
    // profile field never nulls a previously-uploaded avatar.
    const payload = buildPayload(
      ctx.detail.value?.avatarId ?? ctx.profile.value?.avatarId ?? null,
      ctx.detail.value?.avatarUrl ?? null,
    )
    const updated = await ctx.bridge.me.updateProfile(payload)
    ctx.setProfile(updated)
    if (updated) {
      applyProfileToForm(updated)
      mirrorDisplayName(updated)
    }
    ctx.message.success(t('profile.saved'))
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    saving.value = false
  }
}

// ── Avatar ──
async function handleAvatarUpload(file: File | Blob): Promise<{ id?: string; url: string }> {
  savingAvatar.value = true
  try {
    const toUpload =
      file instanceof File ? file : new File([file], 'avatar.png', { type: file.type || 'image/png' })
    const uploaded = await ctx.storage.files.upload(toUpload)
    const id = uploaded?.id
    if (!id) throw new Error(t('profile.avatarUploadFailed'))
    const url = ctx.storage.files.previewUrl(id)

    const updated = await ctx.bridge.me.updateProfile(buildPayload(id, null))
    if (updated) {
      ctx.setProfile(updated)
      applyProfileToForm(updated)
    } else if (ctx.profile.value) {
      ctx.setProfile({ ...ctx.profile.value, avatarId: id, avatar: null })
    }
    ctx.setDetail(ctx.detail.value ? { ...ctx.detail.value, avatarId: id, avatarUrl: null } : ctx.detail.value)
    // Mirror onto the auth store so the header-bar avatar (outside this page) refreshes.
    if (ctx.authStore.userInfo) {
      ctx.authStore.setUserInfo({ ...ctx.authStore.userInfo, avatar: url })
    }
    ctx.message.success(t('profile.avatarUpdated'))
    return { id, url }
  } finally {
    savingAvatar.value = false
  }
}

async function handleAvatarRemove(): Promise<void> {
  savingAvatar.value = true
  try {
    const updated = await ctx.bridge.me.updateProfile(buildPayload(null, null))
    if (updated) {
      ctx.setProfile(updated)
      applyProfileToForm(updated)
    } else if (ctx.profile.value) {
      ctx.setProfile({ ...ctx.profile.value, avatarId: null, avatar: null })
    }
    ctx.setDetail(ctx.detail.value ? { ...ctx.detail.value, avatarId: null, avatarUrl: null } : ctx.detail.value)
    if (ctx.authStore.userInfo) {
      ctx.authStore.setUserInfo({ ...ctx.authStore.userInfo, avatar: undefined })
    }
    ctx.message.success(t('profile.avatarRemoved'))
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    savingAvatar.value = false
  }
}

// ── Change email / phone (two-step verify) ──
interface ChangeModalState {
  show: boolean
  kind: 'email' | 'phone'
  target: string
  code: string
  step: 'send' | 'confirm'
  sending: boolean
  confirming: boolean
}
const changeModal = reactive<ChangeModalState>({
  show: false,
  kind: 'email',
  target: '',
  code: '',
  step: 'send',
  sending: false,
  confirming: false,
})
function openChangeEmail(): void {
  Object.assign(changeModal, { show: true, kind: 'email', target: '', code: '', step: 'send', sending: false, confirming: false })
}
function openChangePhone(): void {
  Object.assign(changeModal, { show: true, kind: 'phone', target: '', code: '', step: 'send', sending: false, confirming: false })
}
const changeModalTitle = computed(() =>
  t(changeModal.kind === 'email' ? 'changeModal.titleEmail' : 'changeModal.titlePhone'),
)
async function sendChangeCode(): Promise<void> {
  if (!changeModal.target.trim()) {
    ctx.message.warning(t('changeModal.targetRequired'))
    return
  }
  changeModal.sending = true
  try {
    await (changeModal.kind === 'email'
      ? ctx.bridge.me.sendChangeEmailCode({ newAddress: changeModal.target.trim() })
      : ctx.bridge.me.sendChangePhoneCode({ newAddress: changeModal.target.trim() }))
    changeModal.step = 'confirm'
    ctx.message.success(t('changeModal.codeSent'))
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    changeModal.sending = false
  }
}
async function confirmChange(): Promise<void> {
  if (!changeModal.code.trim()) {
    ctx.message.warning(t('changeModal.codeRequired'))
    return
  }
  changeModal.confirming = true
  try {
    if (changeModal.kind === 'email') {
      await ctx.bridge.me.confirmChangeEmail({ newEmail: changeModal.target.trim(), code: changeModal.code.trim() })
    } else {
      await ctx.bridge.me.confirmChangePhone({ newPhoneNumber: changeModal.target.trim(), code: changeModal.code.trim() })
    }
    ctx.message.success(t('changeModal.success'))
    changeModal.show = false
    await ctx.loadProfile()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    changeModal.confirming = false
  }
}
</script>
