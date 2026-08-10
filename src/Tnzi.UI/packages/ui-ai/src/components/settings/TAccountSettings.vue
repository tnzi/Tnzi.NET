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
import type { UseAccountSettingsReturn } from '../../headless/useAccountSettings'

const props = defineProps<{
  controller: UseAccountSettingsReturn
}>()

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
const changeDone = ref('')

function openChange(which: 'email' | 'phone'): void {
  changing.value = which
  changeTarget.value = ''
  changeCode.value = ''
  codeSent.value = false
  changeDone.value = ''
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
    changeDone.value = which === 'email' ? 'Email updated.' : 'Phone updated.'
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
  <TSettingGroup title="Profile" :separator="false">
    <TSettingRow label="Display name" description="How you appear in this product.">
      <NInput
        v-model:value="draft.nickname"
        class="t-settings-field__control"
        size="small"
        :maxlength="64"
      />
    </TSettingRow>

    <TSettingRow label="Email" description="A code is sent to the new address to prove you own it.">
      <span class="t-account__contact">
        <span class="t-settings-field__readonly">
          {{ draft.email || 'Not set' }}
        </span>
        <NButton size="tiny" :disabled="controller.busy.value" @click="openChange('email')">
          Change
        </NButton>
      </span>
    </TSettingRow>

    <TSettingRow label="Phone" description="A code is sent to the new number to prove you own it.">
      <span class="t-account__contact">
        <span class="t-settings-field__readonly">
          {{ draft.phoneNumber || 'Not set' }}
        </span>
        <NButton size="tiny" :disabled="controller.busy.value" @click="openChange('phone')">
          Change
        </NButton>
      </span>
    </TSettingRow>

    <!-- Two steps in one row: enter the new address, receive a code there,
         confirm. Collapsed into a single Save it would tell someone their
         address changed when all they proved is they can type. -->
    <TSettingRow
      v-if="changing"
      :label="changing === 'email' ? 'New email' : 'New phone'"
      :description="codeSent ? 'Enter the code we just sent there.' : 'We will send a verification code to it.'"
      stacked
    >
      <div class="t-account__change">
        <NInput
          v-model:value="changeTarget"
          class="t-settings-field__control"
          size="small"
          :disabled="codeSent"
          :placeholder="changing === 'email' ? 'name@example.com' : 'Phone number'"
        />
        <NInput
          v-if="codeSent"
          v-model:value="changeCode"
          class="t-settings-field__control"
          size="small"
          placeholder="Verification code"
        />
        <div class="t-settings-field__actions">
          <NButton size="small" @click="cancelChange">Cancel</NButton>
          <NButton
            v-if="!codeSent"
            size="small"
            type="primary"
            :loading="controller.busy.value"
            :disabled="!changeTarget.trim()"
            @click="sendCode"
          >
            Send code
          </NButton>
          <NButton
            v-else
            size="small"
            type="primary"
            :loading="controller.busy.value"
            :disabled="!changeCode.trim()"
            @click="confirmChange"
          >
            Confirm
          </NButton>
        </div>
      </div>
    </TSettingRow>

    <p v-if="changeDone" class="t-settings-field__hint">{{ changeDone }}</p>

    <p v-if="controller.error.value" class="t-settings-field__error" role="alert">
      {{ controller.error.value }}
    </p>

    <div class="t-settings-field__actions">
      <NButton
        size="small"
        :disabled="!controller.dirty.value || controller.busy.value"
        @click="controller.resetDraft()"
      >
        Reset
      </NButton>
      <NButton
        size="small"
        type="primary"
        :loading="controller.busy.value"
        :disabled="!controller.dirty.value"
        @click="controller.saveProfile()"
      >
        Save
      </NButton>
    </div>
  </TSettingGroup>

  <TSettingGroup title="Password">
    <TSettingRow label="Current password">
      <NInput
        v-model:value="currentPassword"
        class="t-settings-field__control"
        type="password"
        show-password-on="click"
        size="small"
      />
    </TSettingRow>
    <TSettingRow label="New password">
      <NInput
        v-model:value="newPassword"
        class="t-settings-field__control"
        type="password"
        show-password-on="click"
        size="small"
      />
    </TSettingRow>
    <TSettingRow label="Confirm new password">
      <NInput
        v-model:value="confirmPassword"
        class="t-settings-field__control"
        type="password"
        show-password-on="click"
        size="small"
      />
    </TSettingRow>

    <p v-if="mismatch" class="t-settings-field__error" role="alert">
      The two new passwords do not match.
    </p>
    <p v-else-if="passwordDone" class="t-settings-field__hint">Password updated.</p>

    <div class="t-settings-field__actions">
      <NButton
        size="small"
        type="primary"
        :loading="controller.busy.value"
        :disabled="!currentPassword || !newPassword"
        @click="onChangePassword"
      >
        Update password
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
