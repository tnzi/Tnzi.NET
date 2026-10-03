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
 */
import { computed, onMounted, ref } from 'vue'
import { NInput, NButton } from 'naive-ui'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import TStepUpPrompt from './TStepUpPrompt.vue'
import type { UseAccountSettingsReturn } from '../../headless/useAccountSettings'
import { useAiI18n } from '../../i18n'

const props = defineProps<{
  controller: UseAccountSettingsReturn
}>()

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

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const passwordDone = ref(false)

/* Local, because it is a property of these two boxes rather than of the
   request - sending a mismatched pair to find out is a wasted round trip and
   a worse message. */
const mismatch = ref(false)

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
  <TStepUpPrompt v-if="controller.stepUp" :prompt="controller.stepUp" />

  <TSettingGroup :title="t.accountSettings.profileTitle" :separator="false">
    <TSettingRow :label="t.accountSettings.displayName" :description="t.accountSettings.displayNameHint">
      <NInput
        v-model:value="draft.nickname"
        class="t-settings-field__control"
        size="small"
        :maxlength="64"
      />
    </TSettingRow>

    <TSettingRow :label="t.accountSettings.email" :description="t.accountSettings.emailHint">
      <span class="t-account__contact">
        <span class="t-settings-field__readonly">
          {{ draft.email || t.accountSettings.notSet }}
        </span>
        <NButton size="tiny" :disabled="controller.busy.value" @click="openChange('email')">
          {{ t.accountSettings.change }}
        </NButton>
      </span>
    </TSettingRow>

    <TSettingRow :label="t.accountSettings.phone" :description="t.accountSettings.phoneHint">
      <span class="t-account__contact">
        <span class="t-settings-field__readonly">
          {{ draft.phoneNumber || t.accountSettings.notSet }}
        </span>
        <NButton size="tiny" :disabled="controller.busy.value" @click="openChange('phone')">
          {{ t.accountSettings.change }}
        </NButton>
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
        <NInput
          v-model:value="changeTarget"
          class="t-settings-field__control"
          size="small"
          :disabled="codeSent"
          :placeholder="changing === 'email' ? t.accountSettings.emailPlaceholder : t.accountSettings.phonePlaceholder"
        />
        <NInput
          v-if="codeSent"
          v-model:value="changeCode"
          class="t-settings-field__control"
          size="small"
          :placeholder="t.accountSettings.codePlaceholder"
        />
        <div class="t-settings-field__actions">
          <NButton size="small" @click="cancelChange">{{ t.accountSettings.cancel }}</NButton>
          <NButton
            v-if="!codeSent"
            size="small"
            type="primary"
            :loading="controller.busy.value"
            :disabled="!changeTarget.trim()"
            @click="sendCode"
          >
            {{ t.accountSettings.sendCode }}
          </NButton>
          <NButton
            v-else
            size="small"
            type="primary"
            :loading="controller.busy.value"
            :disabled="!changeCode.trim()"
            @click="confirmChange"
          >
            {{ t.accountSettings.confirm }}
          </NButton>
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
      <NButton
        size="small"
        :disabled="!controller.dirty.value || controller.busy.value"
        @click="controller.resetDraft()"
      >
        {{ t.accountSettings.reset }}
      </NButton>
      <NButton
        size="small"
        type="primary"
        :loading="controller.busy.value"
        :disabled="!controller.dirty.value"
        @click="controller.saveProfile()"
      >
        {{ t.accountSettings.save }}
      </NButton>
    </div>
  </TSettingGroup>

  <TSettingGroup :title="t.accountSettings.passwordTitle">
    <TSettingRow :label="t.accountSettings.currentPassword">
      <NInput
        v-model:value="currentPassword"
        class="t-settings-field__control"
        type="password"
        show-password-on="click"
        size="small"
      />
    </TSettingRow>
    <TSettingRow :label="t.accountSettings.newPassword">
      <NInput
        v-model:value="newPassword"
        class="t-settings-field__control"
        type="password"
        show-password-on="click"
        size="small"
      />
    </TSettingRow>
    <TSettingRow :label="t.accountSettings.confirmPassword">
      <NInput
        v-model:value="confirmPassword"
        class="t-settings-field__control"
        type="password"
        show-password-on="click"
        size="small"
      />
    </TSettingRow>

    <p v-if="mismatch" class="t-settings-field__error" role="alert">
      {{ t.accountSettings.passwordMismatch }}
    </p>
    <p v-else-if="passwordDone" class="t-settings-field__hint">{{ t.accountSettings.passwordUpdated }}</p>

    <div class="t-settings-field__actions">
      <NButton
        size="small"
        type="primary"
        :loading="controller.busy.value"
        :disabled="!currentPassword || !newPassword"
        @click="onChangePassword"
      >
        {{ t.accountSettings.updatePassword }}
      </NButton>
    </div>
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
</style>
