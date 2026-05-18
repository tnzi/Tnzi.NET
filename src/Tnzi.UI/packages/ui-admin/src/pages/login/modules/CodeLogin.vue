<script setup lang="ts">
/**
 * `CodeLogin` — mobile/email + verification code login module.
 *
 * Soybean reference: `src/views/_builtin/login/modules/code-login.vue` (66 lines).
 * Endpoints (Tnzi.Identity.DefaultAuthController):
 *   - `POST /auth/code-login/send-code` — `SendCodeLoginCodeDto` → triggers the verification code
 *   - `POST /auth/code-login`           — `CodeLoginDto` → `CodeLoginResultDto`
 *
 * Wired via `useLoginContext().callbacks.sendCode` + `callbacks.codeLogin`.
 */
import { computed, reactive, ref } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpace, type FormRules } from 'naive-ui'
import { useFormRules } from '../../../headless/useFormRules'
import { useNaiveForm } from '../../../headless/useNaiveForm'
import { useCaptcha } from '../../../headless/useCaptcha'
import { useLoginContext } from '../useLoginContext'

defineOptions({ name: 'CodeLogin' })

const { translate, toggleLoginModule, callbacks, helpers } = useLoginContext()
const { rules: r } = useFormRules(translate)
const { formRef, validate } = useNaiveForm()
const { label: codeBtnLabel, isCounting, loading: sending, getCaptcha } = useCaptcha({ translate })

interface FormModel {
  account: string
  code: string
}

const model: FormModel = reactive({ account: '', code: '' })
const submitting = ref(false)
const submitError = ref('')

const rules = computed<FormRules>(() => ({
  // Accept either phone or email — bias toward phone since soybean does.
  account: r.phone,
  code: [
    { required: true, trigger: ['blur', 'input'], message: translate('admin.login.errorEmptyCode', 'Please enter the verification code') },
  ],
}))

async function handleSendCode(): Promise<void> {
  // Validate the account field on its own before firing the send.
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
    await callbacks.sendCode({ account: model.account, purpose: 'code-login' })
  })
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
    await callbacks.codeLogin({ account: model.account, code: model.code }, helpers)
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Login failed')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <NForm ref="formRef" :model="model" :rules="rules" size="large" :show-label="false" @keyup.enter="handleSubmit">
    <NFormItem path="account">
      <NInput v-model:value="model.account" :placeholder="translate('admin.login.phonePlaceholder', 'Enter phone or email')" />
    </NFormItem>
    <NFormItem path="code">
      <div class="w-full flex-y-center gap-16px">
        <NInput v-model:value="model.code" :placeholder="translate('admin.login.codePlaceholder', 'Enter verification code')" />
        <NButton size="large" :disabled="isCounting || sending" :loading="sending" @click="handleSendCode">
          {{ codeBtnLabel }}
        </NButton>
      </div>
    </NFormItem>
    <NSpace vertical :size="18" class="w-full">
      <NButton type="primary" size="large" round block :loading="submitting" @click="handleSubmit">
        {{ translate('admin.login.submit', 'Sign in') }}
      </NButton>
      <NButton size="large" round block @click="toggleLoginModule('pwd-login')">
        {{ translate('admin.login.back', 'Back') }}
      </NButton>
      <p v-if="submitError" class="m-0 text-13px text-error text-center" role="alert">{{ submitError }}</p>
    </NSpace>
  </NForm>
</template>
