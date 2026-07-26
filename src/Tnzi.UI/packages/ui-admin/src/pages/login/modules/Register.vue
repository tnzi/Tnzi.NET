<script setup lang="ts">
/**
 * `Register` - new account registration module.
 *
 * Soybean reference: `src/views/_builtin/login/modules/register.vue` (88 lines).
 * Endpoints (Tnzi.Identity.DefaultAuthController):
 *   - `POST /auth/quick-register/send-code` - `SendQuickRegisterCodeDto`
 *   - `POST /auth/quick-register` - `QuickRegisterDto` (+ set-password), or
 *   - `POST /auth/register` - `RegisterDto` → `TokenResultDto`
 *
 * Wired via `useLoginContext().callbacks.sendCode` (purpose='register') +
 * `callbacks.register`. The account field accepts email OR phone - rule + label
 * adapt to the backend-enabled channels (`features.codeChannels`) and the
 * `type` is auto-detected per submit. On success the page returns to
 * `pwd-login` (the consumer's `register` callback may also navigate elsewhere).
 */
import { computed, reactive, ref, watch } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpace, type FormRules } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useFormRules } from '../../../headless/useFormRules'
import { useNaiveForm } from '../../../headless/useNaiveForm'
import { useCaptcha } from '../../../headless/useCaptcha'
import { useLoginCaptcha } from '../../../headless/useLoginCaptcha'
import { useLoginAccountField } from '../../../headless/useLoginAccountField'
import { detectAccountType } from '../../../headless/accountType'
import { useLoginContext } from '../useLoginContext'
import TLoginCaptcha from './TLoginCaptcha.vue'

defineOptions({ name: 'Register' })

const { translate, toggleLoginModule, callbacks, ui, features } = useLoginContext()
const { rules: r } = useFormRules(translate)
const { formRef, validate } = useNaiveForm()
const { label: codeBtnLabel, isCounting, loading: sending, getCaptcha } = useCaptcha({ translate })
const { rule: accountRule, label: accountLabel, placeholder: accountPlaceholder } = useLoginAccountField(
  translate,
  () => features.codeChannels,
)

// Register image captcha (always-shown when enabled) - gates the send-code step
// so bots can't spam the SMS/email code endpoint. Fetched up-front and refreshed
// after each send (verifying consumes it). Only shown when the backend enabled
// it AND the consumer wired `callbacks.getCaptcha` (so we never show an
// unfetchable field).
const {
  captchaId,
  imageBase64: captchaImage,
  code: captchaCode,
  loading: captchaLoading,
  canRefresh: captchaCanRefresh,
  load: loadCaptcha,
} = useLoginCaptcha('register')
const showCaptcha = computed(() => features.captchaOnRegister && captchaCanRefresh)
watch(
  showCaptcha,
  (v) => {
    if (v && !captchaImage.value) void loadCaptcha()
  },
  { immediate: true },
)

interface FormModel {
  account: string
  code: string
  password: string
  confirmPassword: string
}

const model: FormModel = reactive({ account: '', code: '', password: '', confirmPassword: '' })
const submitting = ref(false)
const submitError = ref('')

const rules = computed<FormRules>(() => ({
  account: accountRule.value,
  code: [
    { required: true, trigger: ['blur', 'input'], message: translate('admin.login.errorEmptyCode', 'Please enter the verification code') },
  ],
  password: r.password(),
  confirmPassword: [
    r.matches(() => model.password, translate('admin.login.errorPasswordMismatch', 'Passwords do not match')),
  ],
}))

async function handleSendCode(): Promise<void> {
  try {
    await formRef.value?.validate(undefined, (rule) => rule.key === 'account')
  } catch {
    return
  }
  const sendCode = callbacks.sendCode
  if (!sendCode) {
    submitError.value = translate(
      'admin.login.errorMissingCallback',
      'Send-code is not configured. Pass `defineAdminApp({ login: { callbacks: { sendCode } } })`.',
    )
    return
  }
  // The register captcha (when enabled) must be solved before the OTP is sent.
  if (showCaptcha.value && !captchaCode.value.trim()) {
    submitError.value = translate('admin.login.captcha.required', 'Please enter the captcha.')
    return
  }
  submitError.value = ''
  try {
    await getCaptcha(async () => {
      await sendCode({
        account: model.account,
        type: detectAccountType(model.account),
        purpose: 'register',
        captchaId: showCaptcha.value ? captchaId.value : undefined,
        captchaCode: showCaptcha.value ? captchaCode.value.trim() : undefined,
      })
    })
  } catch (err) {
    // Surface backend rejections (e.g. 429 "sent too frequently" / wrong captcha)
    // in the UI - getCaptcha re-throws so the countdown never starts on failure.
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Request failed')
  } finally {
    // The captcha is one-time-use (verifying consumes it) - refresh so a resend
    // works whether the send succeeded or the captcha was rejected.
    if (showCaptcha.value) void loadCaptcha()
  }
}

async function handleSubmit(): Promise<void> {
  submitError.value = ''
  try { await validate() } catch { return }
  if (!callbacks.register) {
    submitError.value = translate(
      'admin.login.errorMissingCallback',
      'Register is not configured. Pass `defineAdminApp({ login: { callbacks: { register } } })`.',
    )
    return
  }
  submitting.value = true
  try {
    await callbacks.register({ account: model.account, code: model.code, password: model.password, type: detectAccountType(model.account) })
    // Successful registration → bounce back to pwd-login (the consumer's
    // `register` callback may also navigate elsewhere; this is the safe
    // default that matches soybean's flow).
    toggleLoginModule('pwd-login')
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Registration failed')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <NForm ref="formRef" :model="model" :rules="rules" size="large" :show-label="ui.labeled" :show-require-mark="false" label-placement="top" @keyup.enter="handleSubmit">
    <NFormItem path="account" :label="accountLabel">
      <NInput v-model:value="model.account" :placeholder="accountPlaceholder" />
    </NFormItem>
    <NFormItem v-if="showCaptcha" :label="translate('admin.login.captcha.label', 'Captcha')">
      <TLoginCaptcha
        v-model="captchaCode"
        :image="captchaImage"
        :loading="captchaLoading"
        :refreshable="captchaCanRefresh"
        :placeholder="translate('admin.login.captcha.placeholder', 'Enter the characters shown')"
        :refresh-title="translate('admin.login.captcha.refresh', 'Refresh captcha')"
        @refresh="loadCaptcha"
      />
    </NFormItem>
    <NFormItem path="code" :label="translate('admin.login.labels.code', 'Verification code')">
      <div class="w-full flex-y-center gap-16px">
        <NInput v-model:value="model.code" :placeholder="translate('admin.login.codePlaceholder', 'Enter verification code')" />
        <NButton size="large" :disabled="isCounting || sending" :loading="sending" @click="handleSendCode">
          {{ codeBtnLabel }}
        </NButton>
      </div>
    </NFormItem>
    <NFormItem path="password" :label="translate('admin.login.labels.password', 'Password')">
      <NInput v-model:value="model.password" type="password" show-password-on="click" :placeholder="translate('admin.login.passwordPlaceholder', 'Enter password')" />
    </NFormItem>
    <NFormItem path="confirmPassword" :label="translate('admin.login.labels.confirmPassword', 'Confirm password')">
      <NInput v-model:value="model.confirmPassword" type="password" show-password-on="click" :placeholder="translate('admin.login.confirmPasswordPlaceholder', 'Re-enter password')" />
    </NFormItem>
    <NSpace vertical :size="18" class="w-full">
      <NButton type="primary" size="large" :round="ui.pill" block :loading="submitting" @click="handleSubmit">
        <template #icon>
          <TSvgIcon icon="mdi:account-plus-outline" :size="18" />
        </template>
        {{ translate('admin.login.submitRegister', 'Sign up') }}
      </NButton>
      <NButton size="large" :round="ui.pill" block @click="toggleLoginModule('pwd-login')">
        <template #icon>
          <TSvgIcon icon="mdi:arrow-left" :size="18" />
        </template>
        {{ translate('admin.login.back', 'Back') }}
      </NButton>
      <p v-if="submitError" class="m-0 text-13px text-error text-center" role="alert">{{ submitError }}</p>
    </NSpace>
  </NForm>
</template>
