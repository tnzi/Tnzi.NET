<script setup lang="ts">
/**
 * `PwdLogin` — username + password login module.
 *
 * Soybean reference: `src/views/_builtin/login/modules/pwd-login.vue` (118 lines).
 * Endpoint wired via `useLoginContext().callbacks.pwdLogin`, which the consumer
 * typically backs with `POST /auth/login-with-refresh-token`
 * (`Tnzi.Identity.DefaultAuthController.LoginWithRefreshToken`).
 *
 * 2026-06-11 redesign: form chrome adapts to the shell layout via
 * `useLoginContext().ui` (stacked labels + squared buttons in `split`),
 * demo accounts render as pill chips, and consumer-configured third-party
 * providers render as round icon buttons under an "Or continue with" divider.
 */
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
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
import { TSvgIcon } from '@tnzi/ui'
import { useFormRules } from '../../../headless/useFormRules'
import { useNaiveForm } from '../../../headless/useNaiveForm'
import { useLoginContext, type LoginDemoAccount } from '../useLoginContext'

defineOptions({ name: 'PwdLogin' })

const { translate, toggleLoginModule, callbacks, demoAccounts, ui, thirdParty, scene, helpers } = useLoginContext()
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

// Controlled password visibility (instead of naive's internal
// `show-password-on`) so the split layout's animated characters can react:
// they look away while the password is visible — and sneak the occasional
// peek. The username focus + password length feed the same scene state.
const passwordVisible = ref(false)
watch(passwordVisible, (visible) => {
  scene.passwordVisible = visible
})
watch(
  () => model.password,
  (pwd) => {
    scene.passwordLength = pwd.length
  },
)
onBeforeUnmount(() => {
  scene.typing = false
  scene.passwordVisible = false
  scene.passwordLength = 0
})

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
  <NForm
    ref="formRef"
    :model="model"
    :rules="rules"
    size="large"
    :show-label="ui.labeled"
    :show-require-mark="false"
    label-placement="top"
    @keyup.enter="handleSubmit"
  >
    <NFormItem path="userName" :label="translate('admin.login.labels.userName', 'Username')">
      <NInput
        v-model:value="model.userName"
        :placeholder="translate('admin.login.userNamePlaceholder', 'Enter username')"
        autocomplete="username"
        @focus="scene.typing = true"
        @blur="scene.typing = false"
      />
    </NFormItem>
    <NFormItem path="password" :label="translate('admin.login.labels.password', 'Password')">
      <NInput
        v-model:value="model.password"
        :type="passwordVisible ? 'text' : 'password'"
        :placeholder="translate('admin.login.passwordPlaceholder', 'Enter password')"
        autocomplete="current-password"
      >
        <template #suffix>
          <button
            type="button"
            class="t-pwd-login__eye-toggle"
            :aria-label="passwordVisible ? 'Hide password' : 'Show password'"
            @click="passwordVisible = !passwordVisible"
          >
            <TSvgIcon :icon="passwordVisible ? 'mdi:eye-off-outline' : 'mdi:eye-outline'" :size="18" />
          </button>
        </template>
      </NInput>
    </NFormItem>
    <NSpace vertical :size="24">
      <div class="flex-y-center justify-between">
        <NCheckbox v-model:checked="model.remember">{{ translate('admin.login.rememberMe', 'Remember me') }}</NCheckbox>
        <NButton quaternary @click="toggleLoginModule('reset-pwd')">{{ translate('admin.login.forgotPassword', 'Forgot password?') }}</NButton>
      </div>
      <NButton type="primary" size="large" :round="ui.pill" block :loading="submitting" @click="handleSubmit">
        {{ translate('admin.login.submit', 'Sign in') }}
      </NButton>
      <div class="flex-y-center justify-between gap-12px">
        <NButton class="flex-1" block :round="ui.pill" @click="toggleLoginModule('code-login')">{{ translate('admin.login.codeLogin', 'Code login') }}</NButton>
        <NButton class="flex-1" block :round="ui.pill" @click="toggleLoginModule('register')">{{ translate('admin.login.register', 'Register') }}</NButton>
      </div>
      <template v-if="thirdParty.length > 0">
        <NDivider class="text-14px text-muted !m-0">{{ translate('admin.login.orWith', 'Or continue with') }}</NDivider>
        <div class="flex-center gap-14px" data-test="pwd-login-third-party">
          <NButton
            v-for="provider in thirdParty"
            :key="provider.key"
            circle
            size="large"
            :title="provider.label ?? provider.key"
            :aria-label="provider.label ?? provider.key"
            @click="provider.onClick()"
          >
            <template #icon>
              <TSvgIcon :icon="provider.icon" :size="20" :style="provider.color ? { color: provider.color } : undefined" />
            </template>
          </NButton>
        </div>
      </template>
      <template v-if="demoAccounts.length > 0">
        <NDivider class="text-14px text-muted !m-0">{{ translate('admin.login.demoAccounts', 'Other Account Login') }}</NDivider>
        <div class="flex-center gap-12px">
          <NButton v-for="account in demoAccounts" :key="account.key" round :loading="submitting" @click="handleAccountLogin(account)">
            {{ account.label }}
          </NButton>
        </div>
      </template>
      <p v-if="submitError" class="m-0 text-13px text-error text-center" role="alert">{{ submitError }}</p>
    </NSpace>
  </NForm>
</template>

<style scoped>
.t-pwd-login__eye-toggle {
  display: inline-flex;
  align-items: center;
  border: none;
  background: none;
  padding: 0;
  cursor: pointer;
  color: var(--tnzi-base-text-muted);
  transition: color 0.15s;
}
.t-pwd-login__eye-toggle:hover {
  color: var(--tnzi-base-text);
}
</style>
