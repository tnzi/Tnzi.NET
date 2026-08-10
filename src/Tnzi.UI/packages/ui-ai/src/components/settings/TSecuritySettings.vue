<script setup lang="ts">
/**
 * @experimental
 * TSecuritySettings - two-factor authentication and active sessions.
 *
 * Backed by `/users/profile/two-factor/*` and `/users/profile/sessions`
 * (`Tnzi.Identity` self-service), so an ordinary signed-in user manages their
 * own security here.
 *
 * ★ Suspend and disable are different operations and are offered as such.
 * `suspend` turns the master switch off but keeps the TOTP key and the
 * per-method flags, so `resume` restores the exact setup; `disable` resets the
 * key and clears every method, meaning re-enabling starts from a new QR code.
 * Collapsing them into one "off" switch is how people lose their enrolment
 * while meaning to pause it for an afternoon.
 */
import { onMounted, ref } from 'vue'
import { NQrCode, NInput, NButton } from 'naive-ui'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import type { UseAccountSettingsReturn } from '../../headless/useAccountSettings'

const props = defineProps<{
  controller: UseAccountSettingsReturn
}>()

onMounted(() => {
  void props.controller.load()
  void props.controller.loadSessions()
})

const totpCode = ref('')
const confirmingAllSessions = ref(false)

async function onConfirmTotp(): Promise<void> {
  const ok = await props.controller.confirmTotp(totpCode.value.trim())
  if (ok) totpCode.value = ''
}

async function onRevokeAll(): Promise<void> {
  if (!confirmingAllSessions.value) {
    confirmingAllSessions.value = true
    return
  }
  confirmingAllSessions.value = false
  await props.controller.revokeAllSessions()
}

function formatWhen(value: Date | string | undefined | null): string {
  if (!value) return ''
  const date = value instanceof Date ? value : new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString()
}
</script>

<template>
  <TSettingGroup title="Two-factor authentication" :separator="false">
    <TSettingRow
      label="Authenticator app"
      description="A six-digit code from an app such as 1Password or Google Authenticator."
    >
      <span
        class="t-settings-field__pill"
        :class="{ 't-settings-field__pill--on': controller.twoFactor.value?.isTotpEnabled }"
      >
        {{ controller.twoFactor.value?.isTotpEnabled ? 'On' : 'Off' }}
      </span>
    </TSettingRow>

    <!-- Enrolment. The secret is shown as a QR plus its text form, because a
         phone that cannot scan still has to be able to enrol. -->
    <template v-if="!controller.twoFactor.value?.isTotpEnabled">
      <TSettingRow v-if="controller.totpSetup.value" label="Scan this" stacked>
        <div class="t-security__enrol">
          <NQrCode :value="controller.totpSetup.value.authenticatorUri" :size="152" />
          <div class="t-security__enrol-side">
            <p class="t-settings-field__hint">Can't scan? Enter this key in your app:</p>
            <code class="t-security__secret">{{ controller.totpSetup.value.sharedKey }}</code>
            <NInput
              v-model:value="totpCode"
              size="small"
              :maxlength="8"
              placeholder="6-digit code"
            />
          </div>
        </div>
      </TSettingRow>

      <p v-if="controller.error.value" class="t-settings-field__error" role="alert">
        {{ controller.error.value }}
      </p>

      <div class="t-settings-field__actions">
        <NButton
          v-if="!controller.totpSetup.value"
          size="small"
          type="primary"
          :loading="controller.busy.value"
          @click="controller.beginTotp()"
        >
          Set up
        </NButton>
        <NButton
          v-else
          size="small"
          type="primary"
          :loading="controller.busy.value"
          :disabled="!totpCode.trim()"
          @click="onConfirmTotp"
        >
          Turn on
        </NButton>
      </div>
    </template>

    <!-- Enrolled. Pause and remove are separate on purpose - see the header. -->
    <template v-else>
      <TSettingRow
        v-if="controller.twoFactor.value?.isEnabled === false"
        label="Currently paused"
        description="Your authenticator is still enrolled. Resume to require codes again."
      >
        <NButton size="small" :loading="controller.busy.value" @click="controller.resumeTwoFactor()">
          Resume
        </NButton>
      </TSettingRow>

      <p v-if="controller.error.value" class="t-settings-field__error" role="alert">
        {{ controller.error.value }}
      </p>

      <div class="t-settings-field__actions">
        <NButton
          v-if="controller.twoFactor.value?.isEnabled !== false"
          size="small"
          :loading="controller.busy.value"
          @click="controller.suspendTwoFactor()"
        >
          Pause
        </NButton>
        <NButton size="small" type="error" ghost :loading="controller.busy.value" @click="controller.disableTotp()">
          Remove authenticator
        </NButton>
      </div>
    </template>
  </TSettingGroup>

  <TSettingGroup title="Active sessions">
    <TSettingRow
      v-for="session in controller.sessions.value"
      :key="session.id"
      :label="session.deviceInfo || session.userAgent || 'Unknown device'"
      :description="[session.ipAddress, formatWhen(session.lastActivityTime)].filter(Boolean).join(' · ')"
    >
      <NButton size="small" :loading="controller.busy.value" @click="controller.revokeSession(session.id)">
        Revoke
      </NButton>
    </TSettingRow>

    <p v-if="controller.sessions.value.length === 0" class="t-settings-field__hint">
      No other sessions recorded.
    </p>

    <div class="t-settings-field__actions">
      <NButton
        size="small"
        type="error"
        ghost
        :loading="controller.busy.value"
        :disabled="controller.sessions.value.length === 0"
        @click="onRevokeAll"
      >
        <!-- Says "including this one" because it does: the session DTO carries
             no marker for the current session, so the caller cannot be spared,
             and a button labelled "sign out other devices" would be a promise
             the data cannot keep. -->
        {{ confirmingAllSessions ? 'Sign out everywhere, including this tab?' : 'Sign out everywhere' }}
      </NButton>
    </div>
  </TSettingGroup>
</template>

<style scoped>
.t-security__enrol {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  flex-wrap: wrap;
  padding: 4px 0;
}
.t-security__enrol-side {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 220px;
  flex: 1;
}
.t-security__secret {
  padding: 7px 10px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: var(--tnzi-ai-code-bg);
  color: var(--tnzi-ai-text);
  font-size: 13px;
  letter-spacing: 0.06em;
  word-break: break-all;
}
</style>
