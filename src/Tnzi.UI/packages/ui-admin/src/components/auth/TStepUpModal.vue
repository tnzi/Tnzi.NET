<script setup lang="ts">
/**
 * `TStepUpModal` - the re-authentication prompt for `[RequireStepUp]` actions.
 *
 * Renders a `@tnzi/core` `StepUpPromptController`: the controller owns the
 * state machine (which methods the account can use, which one is in progress,
 * where the code went, what failed) and the HTTP calls; this file is markup.
 * Mount it once next to whatever creates the controller and hand
 * `(scope) => prompt.verify(scope)` to the bridge's `stepUp` dep - the User
 * Center shell does exactly that, so every account section gets the loop
 * without knowing it exists.
 *
 * Flow: the server answers a protected call with `IDENTITY_STEP_UP_REQUIRED`
 * -> `withStepUp` calls `prompt.verify(scope)` -> this modal opens -> the user
 * touches a passkey or types a code -> the grant resolves -> the original call
 * is replayed once. Cancel resolves `null` and the caller sees the original
 * challenge (a visible "not done", not a silent no-op).
 */
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { NButton, NInputOtp, NSpin } from 'naive-ui'
import { TModalShell, TSvgIcon } from '@tnzi/ui'
import { DEFAULT_OTP_CODE_LENGTH, type StepUpMethod, type StepUpPromptController } from '@tnzi/core/services/identity'
import { humanise, translatePageKey } from '../../i18n/translate'

interface Props {
  /** The controller to render. Construct one per screen (or per app). */
  prompt: StepUpPromptController
  /** `(key, fallback?) => string`. Defaults to the bundled `admin.stepUp.*` entries. */
  translate?: (key: string, fallback?: string) => string
  /**
   * Force a digit count for every code method. Leave unset: the controller
   * already knows it per method (`prompt.codeLength`: 6 for the authenticator
   * app, the deployment's `otpCodeLength` for an emailed / texted code).
   */
  codeLength?: number
}

const props = withDefaults(defineProps<Props>(), {
  translate: undefined,
  codeLength: undefined,
})

// `verify()` settles only through this renderer's choose / submitCode /
// cancel. Leaving the screen mid-prompt would otherwise unmount the modal
// with the prompt still open and the calling write (and its `busy`) pending
// until the user found the prompt again; unmounting is a cancel.
onBeforeUnmount(() => {
  if (props.prompt.open) props.prompt.cancel()
})

defineOptions({ name: 'TStepUpModal' })

function defaultTranslate(key: string, fallback?: string): string {
  const hit = translatePageKey('', key)
  if (!hit) return fallback ?? key
  if (fallback && hit === humanise(key)) return fallback
  return hit
}

function t(key: string, fallback?: string): string {
  return (props.translate ?? defaultTranslate)(key, fallback)
}

const otp = ref<string[]>([])
const allowDigit = (value: string) => /^\d*$/.test(value)

// A fresh code entry every time the prompt (re)opens or the method changes -
// a stale half-typed code from the previous attempt must not be replayed.
watch(
  () => [props.prompt.open, props.prompt.method] as const,
  () => {
    otp.value = []
  },
)

const show = computed({
  get: () => props.prompt.open,
  set: (value: boolean) => {
    if (!value) props.prompt.cancel()
  },
})

const METHOD_COPY: Record<StepUpMethod, { key: string; fallback: string; icon: string }> = {
  passkey: { key: 'admin.stepUp.method.passkey', fallback: 'Use a passkey', icon: 'mdi:key-outline' },
  totp: { key: 'admin.stepUp.method.totp', fallback: 'Enter a code from your authenticator app', icon: 'mdi:cellphone-key' },
  sms: { key: 'admin.stepUp.method.sms', fallback: 'Send a code by SMS', icon: 'mdi:cellphone-message' },
  email: { key: 'admin.stepUp.method.email', fallback: 'Send a code by email', icon: 'mdi:email-outline' },
}

function methodLabel(method: StepUpMethod): string {
  const copy = METHOD_COPY[method]
  return t(copy.key, copy.fallback)
}

const codeHint = computed(() => {
  const { method, sentTo } = props.prompt
  if (method === 'totp') {
    return t('admin.stepUp.codeHintTotp', 'Enter the code from your authenticator app.')
  }
  if (sentTo) {
    return t('admin.stepUp.codeSentTo', 'We sent a code to {to}.').replace('{to}', sentTo)
  }
  return t('admin.stepUp.codeSent', 'We sent you a code.')
})

const canResend = computed(() => props.prompt.method === 'sms' || props.prompt.method === 'email')
const code = computed(() => otp.value.join(''))
// An emailed code on a deployment configured for 8 digits does not fit 6 boxes.
const codeLength = computed(() => props.codeLength ?? props.prompt.codeLength ?? DEFAULT_OTP_CODE_LENGTH)

// Keyed on `method`, not `stage`: while a typed code is being verified the
// stage is `busy` but the user is still in the code flow, and flipping back to
// the chooser mid-request would read as "it rejected me". `method` is set only
// once a code path is committed to and cleared by `back()`.
const inCodeFlow = computed(() => props.prompt.method !== null)
const verifying = computed(() => inCodeFlow.value && props.prompt.stage === 'busy')

async function submit(): Promise<void> {
  if (code.value.length < codeLength.value) return
  await props.prompt.submitCode(code.value)
  // A wrong code is reported by the controller; clear the boxes for a retry.
  if (props.prompt.error) otp.value = []
}
</script>

<template>
  <TModalShell
    v-model:show="show"
    :title="t('admin.stepUp.title', 'Confirm it is you')"
    :width="420"
    :mask-closable="false"
    data-test="t-step-up-modal"
  >
    <NSpin :show="prompt.stage === 'loading'">
      <p class="t-step-up__intro">
        {{ t('admin.stepUp.intro', 'This action requires you to verify it is really you before it goes ahead.') }}
      </p>

      <!-- Stage: choose a method -->
      <div v-if="!inCodeFlow" class="t-step-up__methods" data-test="t-step-up-choose">
        <NButton
          v-for="method in prompt.methods"
          :key="method"
          block
          :loading="prompt.stage === 'busy'"
          :disabled="prompt.busy"
          :data-test="`t-step-up-method-${method}`"
          @click="prompt.choose(method)"
        >
          <template #icon><TSvgIcon :icon="METHOD_COPY[method].icon" :size="16" /></template>
          {{ methodLabel(method) }}
        </NButton>
        <p v-if="prompt.stage === 'choose' && !prompt.canVerify" class="t-step-up__empty" data-test="t-step-up-empty">
          {{
            t(
              'admin.stepUp.noMethods',
              'No verification method is available for this account. Add a passkey, or verify your email address or phone number, then try again.',
            )
          }}
        </p>
      </div>

      <!-- Stage: enter the code -->
      <div v-else class="t-step-up__code" data-test="t-step-up-code">
        <p class="t-step-up__hint">{{ codeHint }}</p>
        <div class="t-step-up__otp">
          <NInputOtp
            v-model:value="otp"
            :length="codeLength"
            size="large"
            :allow-input="allowDigit"
            :status="prompt.error ? 'error' : undefined"
            :disabled="prompt.busy"
            @finish="submit"
          />
        </div>
        <div class="t-step-up__links">
          <NButton text size="small" :disabled="prompt.busy" data-test="t-step-up-back" @click="prompt.back()">
            {{ t('admin.stepUp.back', 'Use another method') }}
          </NButton>
          <NButton
            v-if="canResend"
            text
            size="small"
            :disabled="prompt.busy"
            data-test="t-step-up-resend"
            @click="prompt.resendCode()"
          >
            {{ t('admin.stepUp.resend', 'Resend code') }}
          </NButton>
        </div>
      </div>

      <p v-if="prompt.error" class="t-step-up__error" role="alert" data-test="t-step-up-error">{{ prompt.error }}</p>
    </NSpin>

    <template #footer>
      <NButton size="small" :disabled="prompt.stage === 'busy'" data-test="t-step-up-cancel" @click="prompt.cancel()">
        {{ t('admin.stepUp.cancel', 'Cancel') }}
      </NButton>
      <NButton
        v-if="inCodeFlow"
        size="small"
        type="primary"
        :loading="verifying"
        :disabled="code.length < codeLength || prompt.busy"
        data-test="t-step-up-verify"
        @click="submit"
      >
        {{ t('admin.stepUp.verify', 'Verify') }}
      </NButton>
    </template>
  </TModalShell>
</template>

<style scoped>
.t-step-up__intro,
.t-step-up__hint {
  margin: 0 0 12px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-text-secondary);
}
.t-step-up__methods {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-step-up__empty {
  margin: 4px 0 0;
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-text-secondary);
}
.t-step-up__otp {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
}
.t-step-up__links {
  display: flex;
  justify-content: center;
  gap: 16px;
}
.t-step-up__error {
  margin: 12px 0 0;
  font-size: 13px;
  text-align: center;
  color: var(--tnzi-error);
}
</style>
