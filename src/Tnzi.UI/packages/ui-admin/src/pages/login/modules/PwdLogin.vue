<script setup lang="ts">
/**
 * `PwdLogin` — username + password login module.
 *
 * Soybean reference: `src/views/_builtin/login/modules/pwd-login.vue` (118 lines).
 * Endpoint wired via `useLoginContext().callbacks.pwdLogin`, which the consumer
 * typically backs with `POST /auth/login-with-refresh-token`
 * (`Tnzi.Identity.DefaultAuthController.LoginWithRefreshToken`).
 */
import { computed, reactive, ref } from 'vue'
import {
  NForm,
  NFormItem,
  NInput,
  NButton,
  NCheckbox,
  NSpace,
  NDivider,
  type FormRules,
} from 'naive-ui'
import { useFormRules } from '../../../headless/useFormRules'
import { useNaiveForm } from '../../../headless/useNaiveForm'
import { useLoginContext, type LoginDemoAccount } from '../useLoginContext'

defineOptions({ name: 'PwdLogin' })

const { translate, toggleLoginModule, callbacks, demoAccounts, helpers } = useLoginContext()
const { rules: r } = useFormRules(translate)
const { formRef, validate } = useNaiveForm()

interface FormModel {
  userName: string
  password: string
  remember: boolean
}

const model: FormModel = reactive({ userName: '', password: '', remember: true })
const submitting = ref(false)
const submitError = ref('')

const rules = computed<FormRules>(() => ({
  userName: r.userName,
  password: r.password({ min: 1, max: 64 }),
}))

async function handleSubmit(): Promise<void> {
  submitError.value = ''
  try { await validate() } catch { return }
  if (!callbacks.pwdLogin) {
    submitError.value = translate(
      'admin.login.errorMissingCallback',
      'Login is not configured. Pass `defineAdminApp({ login: { callbacks: { pwdLogin } } })`.',
    )
    return
  }
  submitting.value = true
  try {
    await callbacks.pwdLogin(
      { userName: model.userName, password: model.password, remember: model.remember },
      helpers,
    )
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : translate('admin.login.errorGeneric', 'Login failed')
  } finally {
    submitting.value = false
  }
}

async function handleAccountLogin(account: LoginDemoAccount): Promise<void> {
  model.userName = account.userName
  model.password = account.password
  await handleSubmit()
}
</script>

<template>
  <NForm ref="formRef" :model="model" :rules="rules" size="large" :show-label="false" @keyup.enter="handleSubmit">
    <NFormItem path="userName">
      <NInput v-model:value="model.userName" :placeholder="translate('admin.login.userNamePlaceholder', 'Enter username')" autocomplete="username" />
    </NFormItem>
    <NFormItem path="password">
      <NInput v-model:value="model.password" type="password" show-password-on="click" :placeholder="translate('admin.login.passwordPlaceholder', 'Enter password')" autocomplete="current-password" />
    </NFormItem>
    <NSpace vertical :size="24">
      <div class="flex-y-center justify-between">
        <NCheckbox v-model:checked="model.remember">{{ translate('admin.login.rememberMe', 'Remember me') }}</NCheckbox>
        <NButton quaternary @click="toggleLoginModule('reset-pwd')">{{ translate('admin.login.forgotPassword', 'Forgot password?') }}</NButton>
      </div>
      <NButton type="primary" size="large" round block :loading="submitting" @click="handleSubmit">
        {{ translate('admin.login.submit', 'Sign in') }}
      </NButton>
      <div class="flex-y-center justify-between gap-12px">
        <NButton class="flex-1" block @click="toggleLoginModule('code-login')">{{ translate('admin.login.codeLogin', 'Code login') }}</NButton>
        <NButton class="flex-1" block @click="toggleLoginModule('register')">{{ translate('admin.login.register', 'Register') }}</NButton>
      </div>
      <template v-if="demoAccounts.length > 0">
        <NDivider class="text-14px text-muted !m-0">{{ translate('admin.login.demoAccounts', 'Other Account Login') }}</NDivider>
        <div class="flex-center gap-12px">
          <NButton v-for="account in demoAccounts" :key="account.key" type="primary" :loading="submitting" @click="handleAccountLogin(account)">
            {{ account.label }}
          </NButton>
        </div>
      </template>
      <p v-if="submitError" class="m-0 text-13px text-error text-center" role="alert">{{ submitError }}</p>
    </NSpace>
  </NForm>
</template>
