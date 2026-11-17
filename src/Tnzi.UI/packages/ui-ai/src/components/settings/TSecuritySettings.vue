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
import { computed, onMounted, ref } from 'vue'
import { TwoFactorType } from '@tnzi/core/services/identity'
import { isImeComposing } from '@tnzi/core/utils'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import TStepUpPrompt from './TStepUpPrompt.vue'
import TButton from '../controls/TButton.vue'
import TTag from '../controls/TTag.vue'
import TQrCode from '../controls/TQrCode.vue'
import type { UseAccountSettingsReturn } from '../../headless/useAccountSettings'
import { useAiI18n } from '../../i18n'

const props = defineProps<{
  controller: UseAccountSettingsReturn
}>()

const t = useAiI18n()

const emailMethod = computed(() => props.controller.twoFactor.value?.methods?.find((m) => m.type === TwoFactorType.Email) ?? null)
// A deployment that switched passkeys off omits the entry; a browser without
// WebAuthn cannot run the ceremony, so the rows are not offered there either.
const passkeyMethod = computed(() =>
  props.controller.passkeySupported
    ? (props.controller.twoFactor.value?.methods?.find((m) => m.type === TwoFactorType.Passkey) ?? null)
    : null,
)

onMounted(() => {
  void props.controller.load().then(() => {
    if (passkeyMethod.value) void props.controller.loadPasskeys()
  })
  void props.controller.loadSessions()
})

const totpCode = ref('')

function onTotpEnter(event: KeyboardEvent): void {
  if (isImeComposing(event) || !totpCode.value.trim() || props.controller.busy.value) return
  void onConfirmTotp()
}
const confirmingAllSessions = ref(false)
// Two-step confirmation per row. The current session's row carries no Revoke
// at all when the hook can tell which row that is; the confirmation is for
// every other row, because on a deployment without session-bound tokens the
// marker is unknown and any row might still be this device.
const confirmingSessionId = ref<string | null>(null)
const confirmingKeyId = ref<string | null>(null)

async function onConfirmTotp(): Promise<void> {
  const ok = await props.controller.confirmTotp(totpCode.value.trim())
  if (ok) totpCode.value = ''
}

async function onRemoveKey(credentialId: string): Promise<void> {
  if (confirmingKeyId.value !== credentialId) {
    confirmingKeyId.value = credentialId
    return
  }
  confirmingKeyId.value = null
  await props.controller.removePasskey(credentialId)
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
      <TTag :tone="controller.twoFactor.value?.isTotpEnabled ? 'success' : 'neutral'">
        {{ controller.twoFactor.value?.isTotpEnabled ? t.securitySettings.on : t.securitySettings.off }}
      </TTag>
    </TSettingRow>

    <!-- Enrolment. The secret is shown as a QR plus its text form, because a
         phone that cannot scan still has to be able to enrol. -->
    <template v-if="!controller.twoFactor.value?.isTotpEnabled">
      <TSettingRow v-if="controller.totpSetup.value" :label="t.securitySettings.scan" stacked>
        <div class="t-security__enrol">
          <TQrCode
            :value="controller.totpSetup.value.authenticatorUri"
            :label="t.securitySettings.qrLabel"
            data-totp-qr
          />
          <div class="t-security__enrol-side">
            <p class="t-settings-field__hint">{{ t.securitySettings.cantScan }}</p>
            <code class="t-security__secret">{{ controller.totpSetup.value.sharedKey }}</code>
            <input
              v-model="totpCode"
              class="t-input t-input--md t-input--code t-security__code"
              type="text"
              inputmode="numeric"
              autocomplete="one-time-code"
              spellcheck="false"
              maxlength="8"
              :placeholder="t.securitySettings.codePlaceholder"
              :aria-label="t.securitySettings.codePlaceholder"
              @keydown.enter="onTotpEnter"
            />
          </div>
        </div>
      </TSettingRow>

      <div class="t-settings-field__actions">
        <TButton
          v-if="!controller.totpSetup.value"
          variant="primary"
          :loading="controller.busy.value"
          @click="controller.beginTotp()"
        >
          {{ t.securitySettings.setUp }}
        </TButton>
        <TButton
          v-else
          variant="primary"
          :loading="controller.busy.value"
          :disabled="!totpCode.trim()"
          @click="onConfirmTotp"
        >
          {{ t.securitySettings.turnOn }}
        </TButton>
      </div>
    </template>

    <!-- Enrolled. Pause and remove are separate on purpose - see the header. -->
    <template v-else>
      <TSettingRow
        v-if="controller.twoFactor.value?.isEnabled === false"
        :label="t.securitySettings.paused"
        :description="t.securitySettings.pausedHint"
      >
        <TButton :loading="controller.busy.value" @click="controller.resumeTwoFactor()">
          {{ t.securitySettings.resume }}
        </TButton>
      </TSettingRow>

      <div class="t-settings-field__actions">
        <TButton
          v-if="controller.twoFactor.value?.isEnabled !== false"
          :loading="controller.busy.value"
          @click="controller.suspendTwoFactor()"
        >
          {{ t.securitySettings.pause }}
        </TButton>
        <TButton variant="danger" :loading="controller.busy.value" @click="controller.disableTotp()">
          {{ t.securitySettings.remove }}
        </TButton>
      </div>
    </template>

    <!-- The other second factors. Each row exists only when the status endpoint
         lists the method, i.e. the deployment has it switched on. -->
    <TSettingRow
      v-if="emailMethod"
      :label="t.securitySettings.emailCode"
      :description="emailMethod.requiresAddress ? t.securitySettings.emailNeedsAddress : t.securitySettings.emailCodeHint"
    >
      <span class="t-security__method-control">
        <TTag :tone="emailMethod.enabled ? 'success' : 'neutral'">
          {{ emailMethod.enabled ? t.securitySettings.on : t.securitySettings.off }}
        </TTag>
        <TButton
          :variant="emailMethod.enabled ? 'secondary' : 'soft'"
          :loading="controller.busy.value"
          :disabled="!emailMethod.enabled && !emailMethod.available"
          @click="controller.setEmailTwoFactor(!emailMethod.enabled)"
        >
          {{ emailMethod.enabled ? t.securitySettings.turnOff : t.securitySettings.turnOn }}
        </TButton>
      </span>
    </TSettingRow>

    <template v-if="passkeyMethod">
      <TSettingRow :label="t.securitySettings.passkey" :description="t.securitySettings.passkeyHint">
        <span class="t-security__method-control">
          <TTag :tone="passkeyMethod.enabled ? 'success' : 'neutral'">
            {{ passkeyMethod.enabled ? t.securitySettings.on : t.securitySettings.off }}
          </TTag>
          <TButton
            v-if="controller.passkeys.value.length > 0"
            :variant="passkeyMethod.enabled ? 'secondary' : 'soft'"
            :loading="controller.busy.value"
            @click="controller.setPasskeyTwoFactor(!passkeyMethod.enabled)"
          >
            {{ passkeyMethod.enabled ? t.securitySettings.turnOff : t.securitySettings.turnOn }}
          </TButton>
        </span>
      </TSettingRow>

      <TSettingRow
        v-for="key in controller.passkeys.value"
        :key="key.credentialId"
        :label="key.name || t.securitySettings.unnamedPasskey"
        :description="[formatWhen(key.createdAt), key.isBackedUp ? t.securitySettings.passkeyBackedUp : ''].filter(Boolean).join(' · ')"
      >
        <TButton
          :variant="confirmingKeyId === key.credentialId ? 'danger' : 'secondary'"
          :loading="controller.busy.value"
          @click="onRemoveKey(key.credentialId)"
        >
          {{ confirmingKeyId === key.credentialId ? t.securitySettings.removeKeyConfirm : t.securitySettings.removeKey }}
        </TButton>
      </TSettingRow>

      <div class="t-settings-field__actions">
        <TButton :loading="controller.busy.value" @click="controller.addPasskey()">
          {{ controller.passkeys.value.length > 0 ? t.securitySettings.passkeyAddAnother : t.securitySettings.passkeyAdd }}
        </TButton>
      </div>
    </template>

    <p v-if="controller.error.value" class="t-settings-field__error" role="alert">
      {{ controller.error.value }}
    </p>
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
      <TTag v-if="controller.isCurrentSession(session)" tone="accent">
        {{ t.securitySettings.thisDevice }}
      </TTag>
      <TButton
        v-if="!controller.isCurrentSession(session)"
        :variant="confirmingSessionId === session.id ? 'danger' : 'secondary'"
        :loading="controller.busy.value"
        @click="onRevokeSession(session.id)"
      >
        {{ confirmingSessionId === session.id ? t.securitySettings.revokeConfirm : t.securitySettings.revoke }}
      </TButton>
    </TSettingRow>

    <p v-if="controller.sessions.value.length === 0" class="t-settings-field__hint">
      {{ t.securitySettings.noSessions }}
    </p>

    <div class="t-settings-field__actions">
      <!-- Disabled off `otherSessions`: the list always includes the caller's
           own session while signed in, so `sessions.length === 0` never
           disabled anything and the button was live with nobody to sign out. -->
      <TButton
        :variant="confirmingAllSessions ? 'danger' : 'secondary'"
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
      </TButton>
    </div>
  </TSettingGroup>
</template>

<style scoped>
.t-security__method-control {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
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
.t-security__code {
  max-width: 220px;
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
