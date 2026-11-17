<script setup lang="ts">
/**
 * @experimental
 * TAccountSettings - the signed-in user's own profile and password.
 *
 * Backed by `/users/profile/*` (`Tnzi.Identity` self-service), so this is a
 * built-in page rather than something a consumer wires: an ordinary user
 * manages their own account there, no admin permission involved.
 *
 * ★ Email and phone change through a two-step verify-code flow
 * (`/change-email/send-code` then `/confirm`), not through the Save button that
 * writes the display name. The code goes to the NEW address, and receiving it
 * is what proves ownership - a single editable field beside Save would let
 * someone type an address they do not control and be told it worked.
 *
 * ★ The profile photo saves on its own, the moment a picture is chosen: it is
 * a file upload, and holding an uploaded file behind the Save button would
 * leave an orphan behind every "Reset". The controller's `onProfileChange`
 * is where a consumer refreshes the shell's copy of the avatar.
 *
 * ★ Deleting the account asks the user to type the account's address. The
 * button sits at the bottom, behind an explicit second step, and a consumer
 * can hide it (`deletion: false`) or keep it visible but disabled with a
 * reason (`deletion: { blockedReason }`) - "you are the last owner", "cancel
 * your subscription first" - which beats a button that fails after a click.
 */
import { computed, onMounted, ref } from 'vue'
import { TAvatar } from '@tnzi/ui/components'
import { AVATAR_ACCEPTED_TYPES } from '@tnzi/core/services/identity'
import { isImeComposing } from '@tnzi/core/utils'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import TStepUpPrompt from './TStepUpPrompt.vue'
import TImagePicker from '../controls/TImagePicker.vue'
import TButton from '../controls/TButton.vue'
import TIconButton from '../controls/TIconButton.vue'
import type { UseAccountSettingsReturn } from '../../headless/useAccountSettings'
import { useAiI18n, formatAiMessage } from '../../i18n'

const props = withDefaults(
  defineProps<{
    controller: UseAccountSettingsReturn
    /** Show the profile photo row. Needs Tnzi.Storage on the backend. */
    allowAvatar?: boolean
    /**
     * The "Delete account" group. `false` hides it; `{ blockedReason }` keeps
     * it visible with the button disabled and the reason shown.
     */
    deletion?: boolean | { blockedReason?: string | null }
  }>(),
  { allowAvatar: true, deletion: true },
)

const t = useAiI18n()

// See TPersonalizationSettings: a local binding so `vue/no-mutating-props`
// does not read "write through the controller's draft ref" as "reassign a prop".
const draft = computed(() => props.controller.draft.value)

onMounted(() => {
  void props.controller.load()
})

/* Which contact field is mid-change, and how far along. `null` = neither open;
   the two are mutually exclusive so a half-finished email change cannot be
   confused with a half-finished phone one. */
const changing = ref<'email' | 'phone' | null>(null)
const changeTarget = ref('')
const changeCode = ref('')
const codeSent = ref(false)
// Which change just completed. Kept as the field, not the sentence, so the
// confirmation follows a language switch made while it is on screen.
const changeDone = ref<'email' | 'phone' | null>(null)

function openChange(which: 'email' | 'phone'): void {
  changing.value = which
  changeTarget.value = ''
  changeCode.value = ''
  codeSent.value = false
  changeDone.value = null
}

function cancelChange(): void {
  changing.value = null
}

async function sendCode(): Promise<void> {
  const ok = changing.value === 'email'
    ? await props.controller.sendEmailChangeCode(changeTarget.value)
    : await props.controller.sendPhoneChangeCode(changeTarget.value)
  if (ok) codeSent.value = true
}

async function confirmChange(): Promise<void> {
  const which = changing.value
  const ok = which === 'email'
    ? await props.controller.confirmEmailChange(changeTarget.value, changeCode.value)
    : await props.controller.confirmPhoneChange(changeTarget.value, changeCode.value)
  if (ok) {
    changeDone.value = which
    changing.value = null
  }
}

/* Enter in a one-line field runs the step the button beside it would, when
   that button could be pressed. Not while an input method is composing: that
   Enter commits the composed text (Safari sends it after `compositionend`, so
   `isComposing` alone misses it). */
function onEnter(event: KeyboardEvent, ready: boolean, run: () => unknown): void {
  if (isImeComposing(event) || !ready) return
  void run()
}

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const passwordDone = ref(false)

/* Each password box has its own show / hide toggle, so checking what was typed
   in one does not put the others on screen. */
type PasswordBox = 'current' | 'new' | 'confirm'
const revealed = ref<Record<PasswordBox, boolean>>({ current: false, new: false, confirm: false })

const canChangePassword = computed(
  () => Boolean(currentPassword.value && newPassword.value) && !props.controller.busy.value,
)

function toggleReveal(box: PasswordBox): void {
  revealed.value = { ...revealed.value, [box]: !revealed.value[box] }
}

const passwordBoxes = computed(() => [
  { box: 'current' as const, label: t.value.accountSettings.currentPassword, placeholder: t.value.accountSettings.currentPasswordPlaceholder, model: currentPassword, autocomplete: 'current-password' },
  { box: 'new' as const, label: t.value.accountSettings.newPassword, placeholder: t.value.accountSettings.newPasswordPlaceholder, model: newPassword, autocomplete: 'new-password' },
  { box: 'confirm' as const, label: t.value.accountSettings.confirmPassword, placeholder: t.value.accountSettings.confirmPasswordPlaceholder, model: confirmPassword, autocomplete: 'new-password' },
])

/* Local, because it is a property of these two boxes rather than of the
   request - sending a mismatched pair to find out is a wasted round trip and
   a worse message. */
const mismatch = ref(false)

/* ── Profile photo ── */
const acceptTypes = AVATAR_ACCEPTED_TYPES.join(',')
const avatarName = computed(
  () => props.controller.profile.value?.nickname || props.controller.profile.value?.userName || '',
)
const maxMb = computed(() => {
  const mb = props.controller.avatarMaxBytes / (1024 * 1024)
  return Number.isInteger(mb) ? String(mb) : mb.toFixed(1)
})
const avatarHint = computed(() => formatAiMessage(t.value.accountSettings.photoHint, { size: maxMb.value }))
const avatarErrorText = computed(() => {
  const failure = props.controller.avatarError.value
  if (!failure) return null
  if (failure.reason === 'type') return t.value.accountSettings.photoWrongType
  if (failure.reason === 'size') return formatAiMessage(t.value.accountSettings.photoTooLarge, { size: maxMb.value })
  return failure.message || t.value.accountSettings.photoFailed
})

async function onPhotoPicked(file: File): Promise<void> {
  if (props.controller.avatarBusy.value) return
  await props.controller.setAvatar(file)
}

/* ── Delete account ── */
const deletionVisible = computed(() => props.deletion !== false)
const blockedReason = computed(() =>
  typeof props.deletion === 'object' ? props.deletion.blockedReason?.trim() || null : null,
)
const confirmingDelete = ref(false)
const deleteTyped = ref('')
const deleteMatches = computed(() => {
  const target = props.controller.deleteConfirmationTarget.value
  return target !== '' && deleteTyped.value.trim().toLowerCase() === target.toLowerCase()
})
const deletePrompt = computed(() =>
  formatAiMessage(t.value.accountSettings.deleteTypeToConfirm, {
    target: props.controller.deleteConfirmationTarget.value,
  }),
)
const deleteErrorText = computed(() => {
  const failure = props.controller.deleteError.value
  if (!failure) return null
  if (failure.reason === 'mismatch') return t.value.accountSettings.deleteMismatch
  if (failure.errorCode === 'IDENTITY_USER_IN_USE') {
    // The server's sentence names what holds the account; keep it after ours.
    return failure.message
      ? `${t.value.accountSettings.deleteInUse} ${failure.message}`
      : t.value.accountSettings.deleteInUse
  }
  if (failure.errorCode === 'IDENTITY_STEP_UP_REQUIRED') return t.value.accountSettings.deleteNeedsVerification
  return failure.message || t.value.accountSettings.deleteFailed
})

function startDelete(): void {
  confirmingDelete.value = true
  deleteTyped.value = ''
}

function cancelDelete(): void {
  confirmingDelete.value = false
  deleteTyped.value = ''
}

async function confirmDelete(): Promise<void> {
  if (!deleteMatches.value) return
  const ok = await props.controller.deleteAccount(deleteTyped.value)
  if (ok) confirmingDelete.value = false
}

async function onChangePassword(): Promise<void> {
  passwordDone.value = false
  mismatch.value = newPassword.value !== confirmPassword.value
  if (mismatch.value) return

  const ok = await props.controller.changePassword(currentPassword.value, newPassword.value)
  if (ok) {
    currentPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    passwordDone.value = true
  }
}
</script>

<template>
  <!-- Re-authentication for the [RequireStepUp] writes below (confirming an
       email / phone change). See TSecuritySettings for the same mount. -->
  <TStepUpPrompt v-if="controller.stepUp && !controller.deleteBusy.value" :prompt="controller.stepUp" />

  <TSettingGroup :title="t.accountSettings.profileTitle" :separator="false">
    <TSettingRow v-if="allowAvatar" :label="t.accountSettings.photo" :description="avatarHint">
      <TImagePicker
        round
        :has="Boolean(controller.avatarUrl.value)"
        :busy="controller.avatarBusy.value"
        :accept="acceptTypes"
        :change-label="t.accountSettings.photoChange"
        :remove-label="t.accountSettings.photoRemove"
        @pick="onPhotoPicked"
        @remove="controller.removeAvatar()"
      >
        <TAvatar :src="controller.avatarUrl.value" :name="avatarName" :size="40" />
      </TImagePicker>
    </TSettingRow>
    <p v-if="avatarErrorText" class="t-settings-field__error" role="alert">{{ avatarErrorText }}</p>

    <TSettingRow :label="t.accountSettings.displayName" :description="t.accountSettings.displayNameHint">
      <input
        v-model="draft.nickname"
        class="t-input t-settings-field__control"
        type="text"
        maxlength="64"
        autocomplete="nickname"
        :aria-label="t.accountSettings.displayName"
      />
    </TSettingRow>

    <TSettingRow :label="t.accountSettings.email" :description="t.accountSettings.emailHint">
      <span class="t-account__contact">
        <span class="t-settings-field__readonly">
          {{ draft.email || t.accountSettings.notSet }}
        </span>
        <TButton :disabled="controller.busy.value" @click="openChange('email')">
          {{ t.accountSettings.change }}
        </TButton>
      </span>
    </TSettingRow>

    <TSettingRow :label="t.accountSettings.phone" :description="t.accountSettings.phoneHint">
      <span class="t-account__contact">
        <span class="t-settings-field__readonly">
          {{ draft.phoneNumber || t.accountSettings.notSet }}
        </span>
        <TButton :disabled="controller.busy.value" @click="openChange('phone')">
          {{ t.accountSettings.change }}
        </TButton>
      </span>
    </TSettingRow>

    <!-- Two steps in one row: enter the new address, receive a code there,
         confirm. Collapsed into a single Save it would tell someone their
         address changed when all they proved is they can type. -->
    <TSettingRow
      v-if="changing"
      :label="changing === 'email' ? t.accountSettings.newEmail : t.accountSettings.newPhone"
      :description="codeSent ? t.accountSettings.codeSentHint : t.accountSettings.willSendHint"
      stacked
    >
      <div class="t-account__change">
        <input
          v-model="changeTarget"
          class="t-input"
          :type="changing === 'email' ? 'email' : 'tel'"
          :autocomplete="changing === 'email' ? 'email' : 'tel'"
          :disabled="codeSent"
          :placeholder="changing === 'email' ? t.accountSettings.emailPlaceholder : t.accountSettings.phonePlaceholder"
          :aria-label="changing === 'email' ? t.accountSettings.newEmail : t.accountSettings.newPhone"
          @keydown.enter="onEnter($event, !controller.busy.value && Boolean(changeTarget.trim()), sendCode)"
        />
        <input
          v-if="codeSent"
          v-model="changeCode"
          class="t-input t-input--md t-input--code"
          type="text"
          inputmode="numeric"
          autocomplete="one-time-code"
          spellcheck="false"
          :placeholder="t.accountSettings.codePlaceholder"
          :aria-label="t.accountSettings.codePlaceholder"
          @keydown.enter="onEnter($event, !controller.busy.value && Boolean(changeCode.trim()), confirmChange)"
        />
        <div class="t-settings-field__actions">
          <TButton @click="cancelChange">{{ t.accountSettings.cancel }}</TButton>
          <TButton
            v-if="!codeSent"
            variant="primary"
            :loading="controller.busy.value"
            :disabled="!changeTarget.trim()"
            @click="sendCode"
          >
            {{ t.accountSettings.sendCode }}
          </TButton>
          <TButton
            v-else
            variant="primary"
            :loading="controller.busy.value"
            :disabled="!changeCode.trim()"
            @click="confirmChange"
          >
            {{ t.accountSettings.confirm }}
          </TButton>
        </div>
      </div>
    </TSettingRow>

    <p v-if="changeDone" class="t-settings-field__hint">
      {{ changeDone === 'email' ? t.accountSettings.emailUpdated : t.accountSettings.phoneUpdated }}
    </p>

    <p v-if="controller.error.value" class="t-settings-field__error" role="alert">
      {{ controller.error.value }}
    </p>

    <div class="t-settings-field__actions">
      <TButton
        :disabled="!controller.dirty.value || controller.busy.value"
        @click="controller.resetDraft()"
      >
        {{ t.accountSettings.reset }}
      </TButton>
      <TButton
        variant="primary"
        :loading="controller.busy.value"
        :disabled="!controller.dirty.value"
        @click="controller.saveProfile()"
      >
        {{ t.accountSettings.save }}
      </TButton>
    </div>
  </TSettingGroup>

  <TSettingGroup :title="t.accountSettings.passwordTitle">
    <TSettingRow v-for="field in passwordBoxes" :key="field.box" :label="field.label">
      <span class="t-account__secret t-settings-field__control">
        <input
          v-model="field.model.value"
          class="t-input"
          :type="revealed[field.box] ? 'text' : 'password'"
          :autocomplete="field.autocomplete"
          :placeholder="field.placeholder"
          spellcheck="false"
          :aria-label="field.label"
          :aria-invalid="field.box === 'confirm' && mismatch ? 'true' : undefined"
          @keydown.enter="onEnter($event, canChangePassword, onChangePassword)"
        />
        <TIconButton
          class="t-account__reveal"
          size="xs"
          :icon="revealed[field.box] ? 'lucide:eye-off' : 'lucide:eye'"
          :label="revealed[field.box] ? t.accountSettings.hidePassword : t.accountSettings.showPassword"
          :active="revealed[field.box]"
          @click="toggleReveal(field.box)"
        />
      </span>
    </TSettingRow>

    <p v-if="mismatch" class="t-settings-field__error" role="alert">
      {{ t.accountSettings.passwordMismatch }}
    </p>
    <p v-else-if="passwordDone" class="t-settings-field__hint">{{ t.accountSettings.passwordUpdated }}</p>

    <div class="t-settings-field__actions">
      <TButton
        variant="primary"
        :loading="controller.busy.value"
        :disabled="!currentPassword || !newPassword"
        @click="onChangePassword"
      >
        {{ t.accountSettings.updatePassword }}
      </TButton>
    </div>
  </TSettingGroup>

  <TSettingGroup v-if="deletionVisible" :title="t.accountSettings.deleteTitle">
    <TSettingRow :label="t.accountSettings.deleteLabel" :description="blockedReason || t.accountSettings.deleteHint">
      <!-- Opens the confirmation; nothing is deleted yet, so it is not the
           danger button. The one that deletes is, below. -->
      <TButton
        v-if="!confirmingDelete"
        :disabled="Boolean(blockedReason) || !controller.deleteConfirmationTarget.value"
        @click="startDelete"
      >
        {{ t.accountSettings.deleteButton }}
      </TButton>
    </TSettingRow>

    <!-- Re-authentication for this one write renders here, beside the button
         that asked for it, not at the top of a page the user scrolled past. -->
    <TStepUpPrompt v-if="controller.stepUp && controller.deleteBusy.value" :prompt="controller.stepUp" />

    <TSettingRow v-if="confirmingDelete" :label="t.accountSettings.deleteConfirmLabel" :description="deletePrompt" stacked>
      <div class="t-account__change">
        <input
          v-model="deleteTyped"
          class="t-input"
          type="text"
          autocomplete="off"
          spellcheck="false"
          :placeholder="controller.deleteConfirmationTarget.value"
          :aria-label="deletePrompt"
          @keydown.enter="onEnter($event, deleteMatches && !controller.deleteBusy.value, confirmDelete)"
        />
        <div class="t-settings-field__actions">
          <TButton :disabled="controller.deleteBusy.value" @click="cancelDelete">
            {{ t.accountSettings.cancel }}
          </TButton>
          <TButton
            variant="danger"
            :loading="controller.deleteBusy.value"
            :disabled="!deleteMatches"
            @click="confirmDelete"
          >
            {{ t.accountSettings.deleteConfirmButton }}
          </TButton>
        </div>
      </div>
    </TSettingRow>

    <p v-if="deleteErrorText" class="t-settings-field__error" role="alert">{{ deleteErrorText }}</p>
  </TSettingGroup>
</template>

<style scoped>
.t-account__contact {
  display: inline-flex;
  align-items: center;
  gap: 10px;
}
.t-account__change {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: 320px;
}
/* The show / hide toggle sits inside the password box, at its right end; the
   box keeps room for it so long passwords do not run under it. */
.t-account__secret {
  position: relative;
  display: block;
}
.t-account__secret .t-input {
  padding-right: 34px;
}
.t-account__reveal {
  position: absolute;
  top: 1px;
  right: 1px;
}
</style>
