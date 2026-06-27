<script setup lang="ts">
/**
 * `Register` — new account registration module.
 *
 * Soybean reference: `src/views/_builtin/login/modules/register.vue` (88 lines).
 * Endpoints (Tnzi.Identity.DefaultAuthController):
 *   - `POST /auth/quick-register/send-code` — `SendQuickRegisterCodeDto`
 *   - `POST /auth/quick-register`           — `QuickRegisterDto` (+ set-password), or
 *   - `POST /auth/register`                 — `RegisterDto` → `TokenResultDto`
 *
 * Wired via `useLoginContext().callbacks.sendCode` (purpose='register') +
 * `callbacks.register`. The account field accepts email OR phone — rule + label
 * adapt to the backend-enabled channels (`features.codeChannels`) and the
 * `type` is auto-detected per submit. On success the page returns to
 * `pwd-login` (the consumer's `register` callback may also navigate elsewhere).
 */
import { computed, reactive, ref } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpace, type FormRules } from 'naive-ui'
import { useFormRules } from '../../../headless/useFormRules'
import { useNaiveForm } from '../../../headless/useNaiveForm'
import { useCaptcha } from '../../../headless/useCaptcha'
import { useLoginAccountField } from '../../../headless/useLoginAccountField'
import { detectAccountType } from '../../../headless/accountType'
import { useLoginContext } from '../useLoginContext'

defineOptions({ name: 'Register' })

const { translate, toggleLoginModule, callbacks, ui, features } = useLoginContext()
const { rules: r } = useFormRules(translate)
const { formRef, validate } = useNaiveForm()
const { label: codeBtnLabel, isCounting, loading: sending, getCaptcha } = useCaptcha({ translate })
const { rule: accountRule, label: accountLabel, placeholder: accountPlaceholder } = useLoginAccountField(
  translate,
  () => features.codeChannels,
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
  submitError.value = ''
  await getCaptcha(async () => {
    if (!callbacks.sendCode) {
      submitError.value = translate(
        'admin.login.errorMissingCallback',
        'Send-code is not configured. Pass `defineAdminApp({ login: { callbacks: { sendCode } } })`.',
      )
      throw new Error('sendCode callback missing')
    }
    await callbacks.sendCode({ account: model.account, type: detectAccountType(model.account), purpose: 'register' })
  })
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
        {{ translate('admin.login.submitRegister', 'Sign up') }}
      </NButton>
      <NButton size="large" :round="ui.pill" block @click="toggleLoginModule('pwd-login')">
        {{ translate('admin.login.back', 'Back') }}
      </NButton>
      <p v-if="submitError" class="m-0 text-13px text-error text-center" role="alert">{{ submitError }}</p>
    </NSpace>
  </NForm>
</template>
