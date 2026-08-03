<template>
  <TUserCenterSection :title="t('nav.profile')">
    <NForm label-placement="top" :show-feedback="false">
      <!-- Identity row: the picker sits BESIDE the name fields - the picture and
           the name answer the same question, so they read as one block. Only
           this top block shares the row; every field below it stays full width,
           because pairing a 96px avatar with the whole (long) profile form would
           leave a column of dead space under it.
           Avatar upload pushes to the Storage module - hide the picker on hosts
           that never loaded it (v-module is the module twin of v-permission).
           No "Avatar" caption: a circular picker is self-explanatory, and the
           format / size constraint is a hover title on the picker itself. -->
      <div class="t-uc-identity">
        <div v-module="'storage'" class="t-uc-identity__avatar">
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
        </div>
        <div class="t-uc-identity__fields t-uc-form-grid">
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
        </div>
      </div>

      <!-- Identity-core rows. The value ALWAYS renders (even when locked): a
           user who cannot see which address signs them in cannot reason about
           their own account. Only the `Change…` affordance is conditional, and
           it is gated by TWO independent things ANDed together:
             - `capabilities.*Channel` - can this deployment do email/SMS at all
               (derived from the backend auth-channel config);
             - `readonly('email'|'phone')` - may the USER rebind it themselves
               (`userCenter.profile.readonlyFields`, an app-level policy).
           Neither substitutes for the other: turning off the email channel to
           stop self-service rebinding would also take out email login and
           recovery. When locked the button is REMOVED, not disabled - a button
           that does nothing when pressed is worse than no button. -->
      <div class="t-uc-form-grid">
        <NFormItem :label="t('profile.email')">
          <NInput :value="ctx.profile.value?.email ?? ''" disabled :placeholder="t('profile.emailPlaceholder')">
            <template v-if="canChangeEmail" #suffix>
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
            <template v-if="canChangePhone" #suffix>
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

    <!-- Save bar, self-contained placement: the extension block below owns its
         own save button, so this bar must stay attached to the identity fields
         it actually governs - putting it under a foreign block would read as
         "saves everything" while saving only half. -->
    <div v-if="!extraJoined" class="t-uc-save-bar">
      <NButton size="small" @click="resetForm">{{ t('reset') }}</NButton>
      <NButton size="small" type="primary" :loading="saving" @click="save">{{ t('save') }}</NButton>
    </div>

    <!-- Consumer extension block (`userCenter.profile.extra`) - the app's own
         field block, appended to the built-in Profile section. Rendered in ONE
         fixed position (the save bar is what moves around it) so registering
         never remounts it and drops what the user typed.

         Two modes, chosen by the block itself and never by config:
         - self-contained (default): own data, own validation, own save button;
           the framework's Reset/Save neither triggers nor awaits it, so neither
           half can block the other.
         - joined: the block calls `useUserCenterProfileExtra({ save, … })` in
           setup and the single Reset/Save pair below drives BOTH halves. See
           `useUserCenterProfileExtra.ts` for the (non-atomic) save contract.

         It gets no props either way - the section's internals stay out of the
         public contract. -->
    <component :is="extraComponent" v-if="extraComponent" />

    <!-- Save bar, joined placement: it now governs the block above it too, so
         it sits last. Duplicated rather than reordered with CSS on purpose -
         `order` would leave the tab sequence running against the visual one. -->
    <div v-if="extraJoined" class="t-uc-save-bar">
      <!-- Combined unsaved-changes marker: the identity fields plus whatever the
           registered `dirty()` reports. Deliberately an indicator and NOT a gate
           on Save - a block whose `dirty()` reads non-reactive state would
           otherwise lock the user out of saving. -->
      <span v-if="isDirty" class="t-uc-save-bar__dirty">
        <i class="t-uc-save-bar__dot" aria-hidden="true" />
        {{ t('profile.unsaved') }}
      </span>
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
import { computed, defineAsyncComponent, reactive, ref, watch, type Component } from 'vue'
import { NButton, NDatePicker, NForm, NFormItem, NInput, NSelect } from 'naive-ui'
import { TImageUpload } from '@tnzi/ui'
import type { UserDto, UpdateUserDto } from '@tnzi/core/services/identity'
import TUserCenterSection from './TUserCenterSection.vue'
import TModalShell from '../../../components/overlay/TModalShell.vue'
import { vModule } from '../../../directives/vModule'
import { useUserCenterContext } from '../user-center-context'
import {
  createUserCenterProfileExtraRegistry,
  provideUserCenterProfileExtra,
} from '../useUserCenterProfileExtra'
import type { UserCenterProfileField, UserCenterReadonlyField } from '../../../plugin/user-center-config'

const ctx = useUserCenterContext()
const t = ctx.t

// ── Extension-block registry ──
// Provided BEFORE the block renders so it can register from its own setup().
// provide/inject (not a template ref): the block is wrapped in
// defineAsyncComponent, so a ref would resolve to the async wrapper.
const profileExtra = createUserCenterProfileExtraRegistry()
provideUserCenterProfileExtra(profileExtra)
/** A block opted into the framework's single Reset/Save pair. */
const extraJoined = computed(() => profileExtra.handler.value !== null)

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
const readonly = (f: UserCenterReadonlyField) => ctx.isFieldReadonly(f)

/** Self-service rebinding of the login email / phone. Backend channel
 *  capability AND app policy - see the template comment on the identity rows.
 *  The two fields are independent: locking one leaves the other's flow alone. */
const canChangeEmail = computed(() => ctx.capabilities.value.emailChannel && !readonly('email'))
const canChangePhone = computed(() => ctx.capabilities.value.smsChannel && !readonly('phone'))

const genderOptions = computed(() => [
  { label: t('profile.genderUnknown'), value: 0 },
  { label: t('profile.genderMale'), value: 1 },
  { label: t('profile.genderFemale'), value: 2 },
])

// Serialised snapshot of the form as it was last filled from (or written back
// to) the server. Captured inside `applyProfileToForm` - the single place the
// form is seeded - so the comparison can never drift from the mapping.
const pristine = ref('')
const snapshot = (): string => JSON.stringify({ ...form })
const identityDirty = computed(() => snapshot() !== pristine.value)

/** Combined unsaved state: identity fields + whatever a joined block reports.
 *  A block that omits `dirty()` contributes nothing - the framework will not
 *  guess on its behalf (and never blocks Save on the answer either way). */
const isDirty = computed(() => {
  if (identityDirty.value) return true
  const dirty = profileExtra.handler.value?.dirty
  return dirty ? dirty() : false
})

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
  pristine.value = snapshot()
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

/** Reset restores BOTH halves in one click when a block joined - that is the
 *  point of the single Reset/Save pair. An unregistered block is untouched. */
function resetForm(): void {
  if (ctx.profile.value) applyProfileToForm(ctx.profile.value)
  profileExtra.handler.value?.reset?.()
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

const errorText = (e: unknown): string => (e instanceof Error ? e.message : String(e))

/**
 * Save the identity fields, then - when the extension block joined - its fields.
 *
 * ORDER IS PART OF THE CONTRACT and the two writes are NOT atomic: the identity
 * fields belong to the framework's backend (`/users/profile`) while the block's
 * fields normally belong to the app's own. There is no shared transaction, so
 * this is deliberately NOT presented as one:
 *
 * 1. Identity first. If it fails we return WITHOUT calling the handler, so the
 *    only outcome of a failed identity write is "nothing was written anywhere".
 * 2. The handler second. If it fails, the identity half stays committed - it
 *    cannot be rolled back, and pretending otherwise would be a lie. The error
 *    names which half survived instead of a generic "save failed", because the
 *    user has to know what still needs re-entering, and the identity fields are
 *    left re-seeded from the server response (so they stop reading as unsaved).
 */
async function save(): Promise<void> {
  saving.value = true
  try {
    // ── Step 1: identity fields (framework-owned) ──
    // Email / phone deliberately omitted - mutated through the verify-code modal.
    // avatarId/avatarUrl MUST be carried through (REPLACE-semantics) so saving a
    // profile field never nulls a previously-uploaded avatar.
    try {
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
    } catch (e) {
      // Nothing committed on either side - attribute it to the identity half so
      // the user does not go looking through the app's own fields.
      ctx.message.error(
        extraJoined.value
          ? t('profile.saveFailedProfile', { error: errorText(e) })
          : errorText(e),
      )
      return
    }

    // ── Step 2: joined extension block (consumer-owned backend) ──
    const handler = profileExtra.handler.value
    if (handler) {
      try {
        await handler.save()
      } catch (e) {
        // Half-committed on purpose, and said out loud. No rollback attempt:
        // the framework has no compensating write for the app's backend, and a
        // best-effort undo that itself fails would be worse than the truth.
        ctx.message.error(t('profile.saveFailedExtra', { error: errorText(e) }))
        return
      }
    }

    ctx.message.success(t('profile.saved'))
  } finally {
    saving.value = false
  }
}

// ── Consumer extension block ──
// Same source contract as `AdminUserCenterSection.component` (component object
// or plain loader), resolved the same way the shell resolves a section: wrapped
// ONCE here rather than per render, so re-rendering never produces a fresh
// definition (which would remount the block and drop whatever the user typed).
// Resolved at setup because the config is provided at install time and never
// changes. Deliberately rendered WITHOUT props or framework state - keeping the
// section's internals out of the public contract.
function resolveExtra(source: Component | (() => Promise<unknown>) | undefined): Component | null {
  if (!source) return null
  return typeof source === 'function'
    ? defineAsyncComponent(source as () => Promise<{ default: Component }>)
    : (source as Component)
}
const extraComponent = resolveExtra(ctx.config.profile?.extra)

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
