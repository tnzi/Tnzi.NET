<script setup lang="ts">
/**
 * `CodeLogin` - phone/email + verification code login module.
 *
 * Soybean reference: `src/views/_builtin/login/modules/code-login.vue` (66 lines).
 * Endpoints (Tnzi.Identity.DefaultAuthController):
 *   - `POST /auth/code-login/send-code` - `SendCodeLoginCodeDto` → triggers the verification code
 *   - `POST /auth/code-login` - `CodeLoginDto` → `CodeLoginResultDto`
 *
 * Wired via `useLoginContext().callbacks.sendCode` + `callbacks.codeLogin`.
 * The send-code step is gated by the image captcha when the deployment turns on
 * `EnableCaptchaOnLogin` - unlike the password form's adaptive captcha this one
 * is always shown, because every call here spends a real SMS / email.
 * The single account field accepts email OR phone - the rule + label adapt to
 * the backend-enabled channels (`features.codeChannels`) and the `type` is
 * auto-detected per submit so the consumer's callback can route to the right
 * channel.
 */
import { computed, reactive, ref, watch } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpace, type FormRules } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useNaiveForm } from '../../../headless/useNaiveForm'
import { useCaptcha } from '@tnzi/ui'
import { useLoginCaptcha } from '@tnzi/ui'
import { useLoginAccountField } from '@tnzi/ui'
import { detectAccountType } from '../../../headless/account-type'
import { useLoginContext } from '@tnzi/ui'
import TLoginCaptcha from './TLoginCaptcha.vue'

defineOptions({ name: 'CodeLogin' })

const { translate, toggleLoginModule, callbacks, ui, helpers, features } = useLoginContext()
const { formRef, validate } = useNaiveForm()
const { label: codeBtnLabel, isCounting, loading: sending, getCaptcha } = useCaptcha({ translate })
const { rule: accountRule, label: accountLabel, placeholder: accountPlaceholder } = useLoginAccountField(
  translate,
  () => features.codeChannels,
)

// Login image captcha (always shown when enabled) - gates the send-code step so
// bots can't spam the SMS/email endpoint. Only shown when the backend enabled it
// AND the consumer wired `callbacks.getCaptcha`, so we never render a field that
// can't be filled.
const {
  captchaId,
  imageBase64: captchaImage,
  code: captchaCode,
  loading: captchaLoading,
  canRefresh: captchaCanRefresh,
  load: loadCaptcha,
} = useLoginCaptcha('login')
const showCaptcha = computed(() => features.captchaOnLogin && captchaCanRefresh)
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
}

const model: FormModel = reactive({ account: '', code: '' })
const submitting = ref(false)
const submitError = ref('')

const rules = computed<FormRules>(() => ({
  account: accountRule.value,
  code: [
    { required: true, trigger: ['blur', 'input'], message: translate('admin.login.errorEmptyCode', 'Please enter the verification code') },
  ],
}))

async function handleSendCode(): Promise<void> {
  // Validate the account field on its own before firing the send.
  try {
    await formRef.value?.validate(undefined, ['account'])
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
  // The login captcha (when enabled) must be solved before the OTP is sent.
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
        purpose: 'code-login',
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
  if (!callbacks.codeLogin) {
    submitError.value = translate(
      'admin.login.errorMissingCallback',
      'Code-login is not configured. Pass `defineAdminApp({ login: { callbacks: { codeLogin } } })`.',
    )
    return
  }
  submitting.value = true
  try {
    await callbacks.codeLogin({ account: model.account, code: model.code, type: detectAccountType(model.account) }, helpers)
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Login failed')
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
    <NSpace vertical :size="18" class="w-full">
      <NButton type="primary" size="large" :round="ui.pill" block :loading="submitting" @click="handleSubmit">
        <template #icon>
          <TSvgIcon icon="mdi:login-variant" :size="18" />
        </template>
        {{ translate('admin.login.submit', 'Sign in') }}
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
