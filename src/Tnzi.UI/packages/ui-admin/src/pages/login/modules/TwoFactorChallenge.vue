<script setup lang="ts">
/**
 * `TwoFactorChallenge` — second-factor verification module (Phase I.7.5).
 *
 * Endpoints (Tnzi.Identity.DefaultAuthController):
 *   - `POST /auth/verify-2fa`     — `VerifyTwoFactorDto` → `TokenResultDto`
 *   - `POST /auth/send-2fa-code`  — `SendTwoFactorCodeDto` (resend, optional)
 *
 * The module reads the outstanding challenge from
 * `useLoginContext().pendingTwoFactor` — populated by `pwdLogin` /
 * `codeLogin` consumer callbacks via
 * `helpers.setTwoFactorRequired({ challengeId, userName, method })`.
 *
 * No `useFormRules` here — verification codes are usually 6 digits, but
 * the backend tolerates 4-8, so we only enforce non-empty + numeric and
 * let the server reject the rest.
 */
import { computed, reactive, ref } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpace, type FormRules } from 'naive-ui'
import { useNaiveForm } from '../../../headless/useNaiveForm'
import { useCaptcha } from '../../../headless/useCaptcha'
import { useLoginContext } from '../useLoginContext'

defineOptions({ name: 'TwoFactorChallenge' })

const { translate, toggleLoginModule, callbacks, ui, pendingTwoFactor, helpers } = useLoginContext()
const { formRef, validate } = useNaiveForm()
const { label: resendLabel, isCounting, loading: resending, getCaptcha } = useCaptcha({ translate })

const model = reactive({ code: '' })
const submitting = ref(false)
const submitError = ref('')

const challenge = computed(() => pendingTwoFactor.value)
const userLabel = computed(() => challenge.value?.userName ?? '')
const methodLabel = computed(() => {
  const method = challenge.value?.method
  if (method === 'sms') return translate('admin.login.twoFactor.methodSms', 'SMS message')
  if (method === 'email') return translate('admin.login.twoFactor.methodEmail', 'email')
  return translate('admin.login.twoFactor.methodTotp', 'authenticator app')
})

const rules: FormRules = {
  code: [
    { required: true, trigger: ['blur', 'input'], message: translate('admin.login.errorEmptyCode', 'Please enter the verification code') },
    { pattern: /^\d{4,8}$/, trigger: ['blur', 'input'], message: translate('admin.login.errorBadCodeFormat', 'Code must be 4-8 digits') },
  ],
}

async function handleSubmit(): Promise<void> {
  submitError.value = ''
  try { await validate() } catch { return }
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
      code: model.code,
    })
    helpers.clearTwoFactor()
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
    await callbacks.resendTwoFactor({ challengeId: challenge.value?.challengeId })
  })
}

function handleCancel(): void {
  helpers.clearTwoFactor()
  toggleLoginModule('pwd-login')
}
</script>

<template>
  <NForm ref="formRef" :model="model" :rules="rules" size="large" :show-label="ui.labeled" :show-require-mark="false" label-placement="top" @keyup.enter="handleSubmit">
    <p class="m-0 mb-12px text-14px text-muted">
      <template v-if="userLabel">
        {{ translate('admin.login.twoFactor.greeting', 'Hi') }}, <strong class="text-primary">{{ userLabel }}</strong>.
      </template>
      {{ translate('admin.login.twoFactor.prompt', 'Enter the code from your') }} {{ methodLabel }}.
    </p>
    <NFormItem path="code" :label="translate('admin.login.labels.code', 'Verification code')">
      <NInput
        v-model:value="model.code"
        :placeholder="translate('admin.login.twoFactor.codePlaceholder', 'Enter 6-digit code')"
        :maxlength="8"
        :input-props="{ inputmode: 'numeric', autocomplete: 'one-time-code' }"
      />
    </NFormItem>
    <NSpace vertical :size="18" class="w-full">
      <NButton type="primary" size="large" :round="ui.pill" block :loading="submitting" @click="handleSubmit">
        {{ translate('admin.login.twoFactor.verify', 'Verify') }}
      </NButton>
      <NButton
        v-if="callbacks.resendTwoFactor"
        size="large"
        :round="ui.pill"
        block
        :disabled="isCounting || resending"
        :loading="resending"
        @click="handleResend"
      >
        {{ isCounting ? resendLabel : translate('admin.login.twoFactor.resend', 'Resend code') }}
      </NButton>
      <NButton size="large" :round="ui.pill" block @click="handleCancel">
        {{ translate('admin.login.cancel', 'Cancel') }}
      </NButton>
      <p v-if="submitError" class="m-0 text-13px text-error text-center" role="alert">{{ submitError }}</p>
    </NSpace>
  </NForm>
</template>
