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
 * A passkey / security key is the one method without a code: the OTP boxes
 * give way to a single "Use security key" button that runs the WebAuthn
 * ceremony for the challenged account (YubiKey, or the device's own
 * authenticator) and signs in when it completes. Dismissing the system dialog
 * leaves the challenge open so another method can be picked.
 *
 * Endpoints (Tnzi.Identity.DefaultAuthController), reached via consumer callbacks:
 *   - `POST /auth/verify-2fa` - `VerifyTwoFactorDto` → `TokenResultDto`
 *   - `POST /auth/send-2fa-code` - `SendTwoFactorCodeDto` → `{ maskedAddress }`
 *   - `POST /auth/verify-2fa/passkey/begin` + `/complete` - the passkey leg
 *
 * The outstanding challenge is read from `useLoginContext().pendingTwoFactor`,
 * populated by `pwdLogin` / `codeLogin` via `helpers.setTwoFactorRequired(...)`.
 * The OTP boxes size themselves per method: 6 for the authenticator app (fixed
 * by the standard), `features.otpCodeLength` for an emailed / texted code (the
 * deployment's `Identity:Otp:CodeLength`, 4-8, from `GET /auth/config`). Fixed
 * at 6, a deployment configured for 8 sent codes nobody could type in.
 *
 * Once a verification succeeds the module shows "signing you in" until the
 * page unmounts, whatever the challenge does. The wrapped callback starts the
 * post-login navigation without waiting for it, the challenge is cleared
 * right after, and the navigation can take seconds (the target route's chunk
 * loads on demand) - rendering the form for a challenge that no longer exists
 * put an authenticator-app prompt in front of a user who had just passed a
 * security key. A module reached with no challenge at all (a stale link) says
 * so instead of showing an unanswerable form.
 */
import { computed, ref, watch } from 'vue'
import { NButton, NDivider, NInputOtp, NSpace, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useCaptcha } from '@tnzi/ui'
import { useLoginContext, type TwoFactorMethodName } from '@tnzi/ui'
import { codeLengthForMethod, isPasskeySupported } from '@tnzi/core/services/identity'

defineOptions({ name: 'TwoFactorChallenge' })

const { translate, toggleLoginModule, callbacks, ui, pendingTwoFactor, helpers, features } = useLoginContext()
const { label: resendLabel, isCounting, loading: resending, getCaptcha } = useCaptcha({ translate })

// The OTP input binds an array of single chars; join for the callback payload.
const otp = ref<string[]>([])
const code = computed(() => otp.value.join(''))
const submitting = ref(false)
// Set on success and never reset: from here the page is on its way out.
const verified = ref(false)
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
// The passkey leg is a ceremony, not a code: it needs its own callback AND a
// browser that can run it. Offering it where the WebAuthn JSON bridges are
// missing puts a button on screen whose only possible outcome is an error.
const passkeyWired = computed(() => !!callbacks.verifyTwoFactorWithPasskey && isPasskeySupported())
const isPasskey = computed(() => selectedMethod.value === 'passkey')

// The method the user is currently verifying with - starts at the challenge's
// preferred method, switchable to any other enabled method.
const selectedMethod = ref<TwoFactorMethodName>('totp')
// Digits for the method in progress: authenticator 6, delivered code per deployment.
const codeLength = computed(() => codeLengthForMethod(selectedMethod.value, features.otpCodeLength))
watch(
  challenge,
  (c) => {
    const preferred = c?.method ?? 'totp'
    // A passkey challenge with no ceremony wired falls back to the next method
    // the account has; the switcher never lists what cannot be completed.
    const fallback = (c?.methods ?? []).find((m) => m !== 'passkey')
    selectedMethod.value = preferred === 'passkey' && !passkeyWired.value ? (fallback ?? 'totp') : preferred
    maskedAddress.value = c?.maskedAddress ?? ''
    // The login callback already tried to send a code for an initial SMS/email
    // challenge. When that failed, say so (Resend stays available, with no
    // countdown running) instead of claiming a code is on its way.
    const delivered = !!c && (c.method === 'sms' || c.method === 'email')
    submitError.value = delivered && c?.codeSendError ? c.codeSendError : ''
    infoHint.value = delivered && !c?.codeSendError ? codeSentHint() : ''
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
  if (m === 'passkey') return 'mdi:key-outline'
  return 'mdi:cellphone-key'
}
/** Short button label for a switcher option. */
function methodTitle(m: TwoFactorMethodName): string {
  if (m === 'sms') return translate('admin.login.twoFactor.optSms', 'Text message')
  if (m === 'email') return translate('admin.login.twoFactor.optEmail', 'Email')
  if (m === 'passkey') return translate('admin.login.twoFactor.optPasskey', 'Security key / passkey')
  return translate('admin.login.twoFactor.optTotp', 'Authenticator app')
}
function codeSentHint(): string {
  return translate('admin.login.twoFactor.codeSent', 'A new code has been sent.')
}

/**
 * One complete instruction sentence per method; includes the masked target
 * once known. The delivered-code sentences carry `{length}`: the digit count
 * is the deployment's, not a constant (the authenticator one stays "6").
 */
const instruction = computed(() => {
  if (selectedMethod.value === 'totp') {
    return translate('admin.login.twoFactor.promptTotp', 'Enter the 6-digit code from your authenticator app.')
  }
  if (selectedMethod.value === 'passkey') {
    return translate('admin.login.twoFactor.promptPasskey', 'Insert your security key or use your passkey to continue.')
  }
  const length = String(codeLength.value)
  if (maskedAddress.value) {
    return translate('admin.login.twoFactor.promptSentTo', 'Enter the {length}-digit code we sent to {target}.')
      .replace('{target}', maskedAddress.value)
      .replace('{length}', length)
  }
  if (selectedMethod.value === 'sms') {
    return translate('admin.login.twoFactor.promptSms', 'Enter the {length}-digit code we texted to your phone.').replace(
      '{length}',
      length,
    )
  }
  return translate('admin.login.twoFactor.promptEmail', 'Enter the {length}-digit code we sent to your email.').replace(
    '{length}',
    length,
  )
})

// Resend applies only to SMS/email (TOTP and passkey have nothing to deliver)
// and only when the consumer wired the resend callback.
const canResend = computed(
  () => !!callbacks.resendTwoFactor && (selectedMethod.value === 'sms' || selectedMethod.value === 'email'),
)

// Other enabled methods the user can switch to (empty → no switcher). A
// passkey without its callback is not offered: a button that cannot complete
// is worse than no button.
const otherMethods = computed<TwoFactorMethodName[]>(() =>
  (challenge.value?.methods ?? []).filter(
    (m) => m !== selectedMethod.value && (m !== 'passkey' || passkeyWired.value),
  ),
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
  if (m === 'totp' || m === 'passkey' || !callbacks.resendTwoFactor) {
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
  // `NInputOtp` fires `finish` again on any edit of a full field; a second
  // request for the same challenge would race the first one's session.
  if (submitting.value || verified.value) return
  submitError.value = ''
  if (code.value.length < codeLength.value) {
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
    // ★ 传 helpers：2FA 通过之后后端可能紧接着要求改密，那个挑战只能经它交给 shell。
    await callbacks.verifyTwoFactor(
      {
        challengeId: challenge.value?.challengeId,
        code: code.value,
        method: selectedMethod.value,
      },
      helpers,
    )
    verified.value = true
    helpers.clearTwoFactor()
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Verification failed')
    // Clear the boxes so the user can retype immediately after a wrong code.
    otp.value = []
  } finally {
    submitting.value = false
  }
}

/**
 * Run the passkey ceremony for the challenged account. `false` means the user
 * closed the system dialog - the challenge stays as it is, nothing to report.
 */
async function handlePasskey(): Promise<void> {
  if (submitting.value || verified.value) return
  submitError.value = ''
  if (!callbacks.verifyTwoFactorWithPasskey) return
  submitting.value = true
  try {
    const done = await callbacks.verifyTwoFactorWithPasskey({ challengeId: challenge.value?.challengeId }, helpers)
    if (done) {
      verified.value = true
      helpers.clearTwoFactor()
    }
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Verification failed')
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
  <!-- Verified: hold this until the navigation the callback started unmounts the page. -->
  <div v-if="verified" class="t-2fa t-2fa--verified flex flex-col items-center gap-12px py-12px" role="status">
    <NSpin size="small" />
    <p class="m-0 text-14px text-muted">
      {{ translate('admin.login.twoFactor.verified', 'Verified. Signing you in...') }}
    </p>
  </div>

  <!-- No challenge to answer (a stale link, a refreshed page): say so, offer the way back. -->
  <div v-else-if="!challenge" class="t-2fa t-2fa--none flex flex-col gap-16px">
    <p class="m-0 text-14px text-muted text-center">
      {{ translate('admin.login.twoFactor.nonePending', 'There is no verification pending. Sign in again to continue.') }}
    </p>
    <NButton size="large" :round="ui.pill" block @click="handleCancel">
      <template #icon>
        <TSvgIcon icon="mdi:arrow-left" :size="18" />
      </template>
      {{ translate('admin.login.back', 'Back') }}
    </NButton>
  </div>

  <div v-else class="t-2fa flex flex-col gap-16px">
    <!-- Instruction: one full sentence per method + optional account line. -->
    <div class="text-center">
      <p class="m-0 text-14px text-muted">{{ instruction }}</p>
      <p v-if="userLabel" class="m-0 mt-4px text-13px text-muted">
        {{ translate('admin.login.twoFactor.signingInAs', 'Signing in as') }}
        <strong class="t-2fa__account">{{ userLabel }}</strong>
      </p>
    </div>

    <!-- OTP boxes (auto-submit on the last digit); a passkey has no code. -->
    <div v-if="!isPasskey" class="flex-center">
      <NInputOtp
        v-model:value="otp"
        :length="codeLength"
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
      <NButton v-if="isPasskey" type="primary" size="large" :round="ui.pill" block :loading="submitting" @click="handlePasskey">
        <template #icon>
          <TSvgIcon icon="mdi:key-outline" :size="18" />
        </template>
        {{ translate('admin.login.twoFactor.usePasskey', 'Use security key') }}
      </NButton>
      <NButton v-else type="primary" size="large" :round="ui.pill" block :loading="submitting" @click="handleSubmit">
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
