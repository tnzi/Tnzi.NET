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
import TStepUpPrompt from './TStepUpPrompt.vue'
import type { UseAccountSettingsReturn } from '../../headless/useAccountSettings'
import { useAiI18n } from '../../i18n'

const props = defineProps<{
  controller: UseAccountSettingsReturn
}>()

const t = useAiI18n()

onMounted(() => {
  void props.controller.load()
  void props.controller.loadSessions()
})

const totpCode = ref('')
const confirmingAllSessions = ref(false)
// Two-step confirmation per row. The current session's row carries no Revoke
// at all when the hook can tell which row that is; the confirmation is for
// every other row, because on a deployment without session-bound tokens the
// marker is unknown and any row might still be this device.
const confirmingSessionId = ref<string | null>(null)

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

async function onRevokeSession(sessionId: string): Promise<void> {
  if (confirmingSessionId.value !== sessionId) {
    confirmingSessionId.value = sessionId
    return
  }
  confirmingSessionId.value = null
  await props.controller.revokeSession(sessionId)
}

function formatWhen(value: Date | string | undefined | null): string {
  if (!value) return ''
  const date = value instanceof Date ? value : new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString()
}
</script>

<template>
  <!-- Re-authentication for the [RequireStepUp] writes below (pause / remove
       authenticator). The controller runs every write through the loop; this
       renders the prompt where the user can still see the row they acted on. -->
  <TStepUpPrompt v-if="controller.stepUp" :prompt="controller.stepUp" />

  <TSettingGroup :title="t.securitySettings.twoFactorTitle" :separator="false">
    <TSettingRow
      :label="t.securitySettings.authenticator"
      :description="t.securitySettings.authenticatorHint"
    >
      <span
        class="t-settings-field__pill"
        :class="{ 't-settings-field__pill--on': controller.twoFactor.value?.isTotpEnabled }"
      >
        {{ controller.twoFactor.value?.isTotpEnabled ? t.securitySettings.on : t.securitySettings.off }}
      </span>
    </TSettingRow>

    <!-- Enrolment. The secret is shown as a QR plus its text form, because a
         phone that cannot scan still has to be able to enrol. -->
    <template v-if="!controller.twoFactor.value?.isTotpEnabled">
      <TSettingRow v-if="controller.totpSetup.value" :label="t.securitySettings.scan" stacked>
        <div class="t-security__enrol">
          <NQrCode :value="controller.totpSetup.value.authenticatorUri" :size="152" />
          <div class="t-security__enrol-side">
            <p class="t-settings-field__hint">{{ t.securitySettings.cantScan }}</p>
            <code class="t-security__secret">{{ controller.totpSetup.value.sharedKey }}</code>
            <NInput
              v-model:value="totpCode"
              size="small"
              :maxlength="8"
              :placeholder="t.securitySettings.codePlaceholder"
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
          {{ t.securitySettings.setUp }}
        </NButton>
        <NButton
          v-else
          size="small"
          type="primary"
          :loading="controller.busy.value"
          :disabled="!totpCode.trim()"
          @click="onConfirmTotp"
        >
          {{ t.securitySettings.turnOn }}
        </NButton>
      </div>
    </template>

    <!-- Enrolled. Pause and remove are separate on purpose - see the header. -->
    <template v-else>
      <TSettingRow
        v-if="controller.twoFactor.value?.isEnabled === false"
        :label="t.securitySettings.paused"
        :description="t.securitySettings.pausedHint"
      >
        <NButton size="small" :loading="controller.busy.value" @click="controller.resumeTwoFactor()">
          {{ t.securitySettings.resume }}
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
          {{ t.securitySettings.pause }}
        </NButton>
        <NButton size="small" type="error" ghost :loading="controller.busy.value" @click="controller.disableTotp()">
          {{ t.securitySettings.remove }}
        </NButton>
      </div>
    </template>
  </TSettingGroup>

  <TSettingGroup :title="t.securitySettings.sessionsTitle">
    <TSettingRow
      v-for="session in controller.sessions.value"
      :key="session.id"
      :label="session.deviceInfo || session.userAgent || t.securitySettings.unknownDevice"
      :description="[session.ipAddress, formatWhen(session.lastActivityTime)].filter(Boolean).join(' · ')"
    >
      <!-- The row this tab is signed in with is labelled instead of revocable:
           revoking it signs the user out of the page they are standing on. -->
      <span
        v-if="controller.isCurrentSession(session)"
        class="t-settings-field__pill t-settings-field__pill--on"
      >
        {{ t.securitySettings.thisDevice }}
      </span>
      <NButton
        v-if="!controller.isCurrentSession(session)"
        size="small"
        :type="confirmingSessionId === session.id ? 'error' : undefined"
        :loading="controller.busy.value"
        @click="onRevokeSession(session.id)"
      >
        {{ confirmingSessionId === session.id ? t.securitySettings.revokeConfirm : t.securitySettings.revoke }}
      </NButton>
    </TSettingRow>

    <p v-if="controller.sessions.value.length === 0" class="t-settings-field__hint">
      {{ t.securitySettings.noSessions }}
    </p>

    <div class="t-settings-field__actions">
      <!-- Disabled off `otherSessions`: the list always includes the caller's
           own session while signed in, so `sessions.length === 0` never
           disabled anything and the button was live with nobody to sign out. -->
      <NButton
        size="small"
        type="error"
        ghost
        :loading="controller.busy.value"
        :disabled="controller.otherSessions.value.length === 0"
        @click="onRevokeAll"
      >
        <!-- `revokeAllSessions()` keeps the caller's own session (the backend
             excludes it unless asked), so the button says exactly that. A true
             "sign out everywhere" would pass `true` AND have to clear local
             auth / leave the page, which this component has no access to;
             that is a product decision, not a label. -->
        {{ confirmingAllSessions ? t.securitySettings.signOutOthersConfirm : t.securitySettings.signOutOthers }}
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
