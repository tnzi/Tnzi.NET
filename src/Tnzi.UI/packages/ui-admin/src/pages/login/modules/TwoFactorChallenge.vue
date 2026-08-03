<script setup lang="ts">
/**
 * `TwoFactorChallenge` - second-factor verification module.
 *
 * Design language: matches its sibling login modules (PwdLogin / CodeLogin) -
 * lean naive-ui primitives (`NInputOtp` / `NButton` / `NSpace` / `NDivider`)
 * over UnoCSS atoms, no bespoke chrome. The shell (`TLoginPage`) already renders
 * the "Two-Factor Verification" heading, so this module only shows the body:
 *   - one full-sentence instruction (per method; includes the masked
 *     destination - `j***@example.com` - once known, so the user knows where
 *     to look and that the code was actually sent),
 *   - an optional "Signing in as <name>" account confirmation,
 *   - the OTP boxes, feedback line, Verify + Back actions (with icons),
 *   - a resend affordance (SMS / email only) and a "Try another way" switcher.
 *
 * Endpoints (Tnzi.Identity.DefaultAuthController), reached via consumer callbacks:
 *   - `POST /auth/verify-2fa` - `VerifyTwoFactorDto` → `TokenResultDto`
 *   - `POST /auth/send-2fa-code` - `SendTwoFactorCodeDto` → `{ maskedAddress }`
 *
 * The outstanding challenge is read from `useLoginContext().pendingTwoFactor`,
 * populated by `pwdLogin` / `codeLogin` via `helpers.setTwoFactorRequired(...)`.
 * The OTP boxes are fixed at 6; the backend tolerates 4-8, so we only guard
 * non-empty and let the server reject the rest.
 */
import { computed, ref, watch } from 'vue'
import { NButton, NDivider, NInputOtp, NSpace } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useCaptcha } from '@tnzi/ui'
import { useLoginContext, type TwoFactorMethodName } from '@tnzi/ui'

defineOptions({ name: 'TwoFactorChallenge' })

const { translate, toggleLoginModule, callbacks, ui, pendingTwoFactor, helpers } = useLoginContext()
const { label: resendLabel, isCounting, loading: resending, getCaptcha } = useCaptcha({ translate })

// The OTP input binds an array of single chars; join for the callback payload.
const otp = ref<string[]>([])
const code = computed(() => otp.value.join(''))
const submitting = ref(false)
const submitError = ref('')
const infoHint = ref('')
// Masked destination (e.g. `j***@example.com`) the code was sent to.
const maskedAddress = ref('')
// The method currently being switched to (SMS/email deliver a code) → drives
// the per-option loading spinner in the switcher.
const switchingTo = ref<TwoFactorMethodName | null>(null)

/** Restrict every OTP box to a single digit. */
function allowDigit(char: string): boolean {
  return /^\d?$/.test(char)
}

const challenge = computed(() => pendingTwoFactor.value)
const userLabel = computed(() => challenge.value?.userName ?? '')

// The method the user is currently verifying with - starts at the challenge's
// preferred method, switchable to any other enabled method.
const selectedMethod = ref<TwoFactorMethodName>('totp')
watch(
  challenge,
  (c) => {
    selectedMethod.value = c?.method ?? 'totp'
    maskedAddress.value = c?.maskedAddress ?? ''
    // pwdLogin already sent a code for an initial SMS/email challenge.
    infoHint.value = c && c.method && c.method !== 'totp' ? codeSentHint() : ''
  },
  { immediate: true },
)

// Once the user starts typing the code, dismiss the "code sent" hint / any
// error - the status line has served its purpose and shouldn't linger.
watch(code, (value) => {
  if (value.length > 0) {
    infoHint.value = ''
    submitError.value = ''
  }
})

/** Iconify glyph for a method - used in the "try another way" switcher. */
function methodIcon(m: TwoFactorMethodName): string {
  if (m === 'sms') return 'mdi:message-text-outline'
  if (m === 'email') return 'mdi:email-outline'
  return 'mdi:cellphone-key'
}
/** Short button label for a switcher option. */
function methodTitle(m: TwoFactorMethodName): string {
  if (m === 'sms') return translate('admin.login.twoFactor.optSms', 'Text message')
  if (m === 'email') return translate('admin.login.twoFactor.optEmail', 'Email')
  return translate('admin.login.twoFactor.optTotp', 'Authenticator app')
}
function codeSentHint(): string {
  return translate('admin.login.twoFactor.codeSent', 'A new code has been sent.')
}

/** One complete instruction sentence per method; includes the masked target once known. */
const instruction = computed(() => {
  if (selectedMethod.value === 'totp') {
    return translate('admin.login.twoFactor.promptTotp', 'Enter the 6-digit code from your authenticator app.')
  }
  if (maskedAddress.value) {
    return translate('admin.login.twoFactor.promptSentTo', 'Enter the 6-digit code we sent to {target}.').replace(
      '{target}',
      maskedAddress.value,
    )
  }
  if (selectedMethod.value === 'sms') {
    return translate('admin.login.twoFactor.promptSms', 'Enter the 6-digit code we texted to your phone.')
  }
  return translate('admin.login.twoFactor.promptEmail', 'Enter the 6-digit code we sent to your email.')
})

// Resend applies only to SMS/email (TOTP has nothing to deliver) and only when
// the consumer wired the resend callback.
const canResend = computed(() => !!callbacks.resendTwoFactor && selectedMethod.value !== 'totp')

// Other enabled methods the user can switch to (empty → no switcher).
const otherMethods = computed<TwoFactorMethodName[]>(() =>
  (challenge.value?.methods ?? []).filter((m) => m !== selectedMethod.value),
)

/**
 * Switch the active method. TOTP switches instantly (nothing to send); for
 * SMS/email the code is delivered FIRST (the option button spins), and the
 * view only switches once the send succeeds - so the user never lands on an
 * empty "email" screen wondering whether a code was sent. A failed send keeps
 * the current method and surfaces the error.
 */
async function switchMethod(m: TwoFactorMethodName): Promise<void> {
  if (m === selectedMethod.value || switchingTo.value) return
  if (m === 'totp' || !callbacks.resendTwoFactor) {
    selectedMethod.value = m
    otp.value = []
    submitError.value = ''
    infoHint.value = ''
    maskedAddress.value = ''
    return
  }
  switchingTo.value = m
  submitError.value = ''
  try {
    const res = await callbacks.resendTwoFactor({ challengeId: challenge.value?.challengeId, method: m })
    selectedMethod.value = m
    otp.value = []
    maskedAddress.value = res?.maskedAddress ?? ''
    infoHint.value = codeSentHint()
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Failed to send the code')
  } finally {
    switchingTo.value = null
  }
}

async function handleSubmit(): Promise<void> {
  submitError.value = ''
  if (code.value.length < 4) {
    submitError.value = translate('admin.login.errorEmptyCode', 'Please enter the verification code')
    return
  }
  if (!callbacks.verifyTwoFactor) {
    submitError.value = translate(
      'admin.login.errorMissingCallback',
      'Two-factor verification is not configured. Pass `defineAdminApp({ login: { callbacks: { verifyTwoFactor } } })`.',
    )
    return
  }
  submitting.value = true
  try {
    await callbacks.verifyTwoFactor({
      challengeId: challenge.value?.challengeId,
      code: code.value,
      method: selectedMethod.value,
    })
    helpers.clearTwoFactor()
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Verification failed')
    // Clear the boxes so the user can retype immediately after a wrong code.
    otp.value = []
  } finally {
    submitting.value = false
  }
}

async function handleResend(): Promise<void> {
  submitError.value = ''
  await getCaptcha(async () => {
    if (!callbacks.resendTwoFactor) {
      submitError.value = translate(
        'admin.login.errorMissingResend',
        'Resending the code is not configured for this consumer.',
      )
      throw new Error('resendTwoFactor callback missing')
    }
    const res = await callbacks.resendTwoFactor({ challengeId: challenge.value?.challengeId, method: selectedMethod.value })
    maskedAddress.value = res?.maskedAddress ?? maskedAddress.value
    otp.value = []
    infoHint.value = codeSentHint()
  })
}

function handleCancel(): void {
  helpers.clearTwoFactor()
  toggleLoginModule('pwd-login')
}
</script>

<template>
  <div class="t-2fa flex flex-col gap-16px">
    <!-- Instruction: one full sentence per method + optional account line. -->
    <div class="text-center">
      <p class="m-0 text-14px text-muted">{{ instruction }}</p>
      <p v-if="userLabel" class="m-0 mt-4px text-13px text-muted">
        {{ translate('admin.login.twoFactor.signingInAs', 'Signing in as') }}
        <strong class="t-2fa__account">{{ userLabel }}</strong>
      </p>
    </div>

    <!-- OTP boxes (auto-submit on the 6th digit). -->
    <div class="flex-center">
      <NInputOtp
        v-model:value="otp"
        :length="6"
        size="large"
        :allow-input="allowDigit"
        :status="submitError ? 'error' : undefined"
        @finish="handleSubmit"
      />
    </div>

    <!-- Feedback line - same pattern as the sibling modules. -->
    <p v-if="submitError" class="m-0 text-13px text-error text-center" role="alert">{{ submitError }}</p>
    <p v-else-if="infoHint" class="m-0 text-13px text-primary text-center">{{ infoHint }}</p>

    <!-- Resend (SMS/email only) - inline text link with countdown. -->
    <div v-if="canResend" class="flex-center flex-wrap gap-8px text-13px text-muted">
      <span>{{ translate('admin.login.twoFactor.noCode', "Didn't get a code?") }}</span>
      <NButton
        text
        type="primary"
        size="small"
        :disabled="isCounting || resending"
        :loading="resending"
        @click="handleResend"
      >
        <template v-if="!isCounting && !resending" #icon>
          <TSvgIcon icon="mdi:refresh" :size="15" />
        </template>
        {{ isCounting ? resendLabel : translate('admin.login.twoFactor.resend', 'Resend') }}
      </NButton>
    </div>

    <!-- Primary actions - mirrors CodeLogin (full-width stacked buttons). -->
    <NSpace vertical :size="18" class="w-full">
      <NButton type="primary" size="large" :round="ui.pill" block :loading="submitting" @click="handleSubmit">
        <template #icon>
          <TSvgIcon icon="mdi:shield-check-outline" :size="18" />
        </template>
        {{ translate('admin.login.twoFactor.verify', 'Verify') }}
      </NButton>
      <NButton size="large" :round="ui.pill" block @click="handleCancel">
        <template #icon>
          <TSvgIcon icon="mdi:arrow-left" :size="18" />
        </template>
        {{ translate('admin.login.back', 'Back') }}
      </NButton>
    </NSpace>

    <!-- Switch to another enabled method (e.g. can't reach the authenticator). -->
    <div v-if="otherMethods.length" class="flex flex-col gap-8px">
      <NDivider class="!m-0 text-13px text-muted">
        {{ translate('admin.login.twoFactor.tryAnother', 'Try another way') }}
      </NDivider>
      <NButton
        v-for="m in otherMethods"
        :key="m"
        block
        secondary
        :round="ui.pill"
        :loading="switchingTo === m"
        @click="switchMethod(m)"
      >
        <template #icon>
          <TSvgIcon :icon="methodIcon(m)" :size="18" />
        </template>
        {{ methodTitle(m) }}
      </NButton>
    </div>
  </div>
</template>

<style scoped>
.t-2fa__account {
  color: var(--tnzi-base-text);
  font-weight: 600;
}
</style>
