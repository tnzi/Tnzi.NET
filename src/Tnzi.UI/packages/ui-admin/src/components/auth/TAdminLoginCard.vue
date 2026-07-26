<script setup lang="ts">
/**
 * `TAdminLoginCard` - soybean-style production login card.
 *
 * Provides the visual + form layout. Consumer wires the auth call via the
 * `onLogin` prop (returning a Promise).
 *
 * `variant` - `'page'` (default, legacy: wraps card in a full-page main + radial
 * background) or `'standalone'` (just the NCard, useful when TLoginPage owns
 * the outer chrome).
 *
 * Validation uses naive-ui's `NForm` `:rules`. Errors render inline under each
 * field; submit error from `onLogin` rejection shows under the button.
 */
import { reactive, ref, computed } from 'vue'
import {
  NCard,
  NForm,
  NFormItem,
  NInput,
  NButton,
  NCheckbox,
  NTabs,
  NTabPane,
  NDivider,
  type FormInst,
  type FormRules,
} from 'naive-ui'

export interface DemoAccount {
  label: string
  userName: string
  password: string
  description?: string
}

export interface LoginPayload {
  method: 'pwd' | 'code'
  userName: string
  password: string
  remember: boolean
}

export type TAdminLoginCardVariant = 'page' | 'standalone'

interface Props {
  /** Outer chrome variant. `'standalone'` skips the full-page wrapper. */
  variant?: TAdminLoginCardVariant
  title?: string
  subtitle?: string
  /** Demo accounts shown as quick-fill cards under the form. */
  demoAccounts?: DemoAccount[]
  /** Enable the SMS-code tab. Defaults to false (pwd only). */
  enableCodeLogin?: boolean
  /** Pre-fill credentials on mount (one-click demo). */
  defaultUserName?: string
  defaultPassword?: string
  /** Translation function. Defaults to identity (returns key). */
  translate?: (key: string) => string
  /** Called when the user submits. Resolves on success; rejection message surfaces under button. */
  onLogin: (payload: LoginPayload) => Promise<void>
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'page',
  title: 'Sign in',
  subtitle: undefined,
  demoAccounts: () => [],
  enableCodeLogin: false,
  defaultUserName: '',
  defaultPassword: '',
  translate: undefined,
  onLogin: undefined as unknown as Props['onLogin'],
})

const emit = defineEmits<{
  success: []
  error: [message: string]
}>()

const method = ref<'pwd' | 'code'>('pwd')
const submitting = ref(false)
const submitError = ref('')
const formRef = ref<FormInst | null>(null)

const form = reactive<LoginPayload>({
  method: 'pwd',
  userName: props.defaultUserName,
  password: props.defaultPassword,
  remember: true,
})

function t(key: string, fallback?: string): string {
  if (props.translate) {
    const result = props.translate(key)
    return result === key && fallback ? fallback : result
  }
  return fallback ?? key
}

const submitLabel = computed(() => t('admin.login.submit', 'Sign in'))
const codeBtnLabel = computed(() => t('admin.login.sendCode', 'Send code'))

const rules = computed<FormRules>(() => ({
  userName: [
    {
      required: true,
      trigger: ['blur', 'input'],
      message: t('admin.login.errorEmptyUsername', 'Please enter username'),
    },
  ],
  password: [
    {
      required: true,
      trigger: ['blur', 'input'],
      message:
        method.value === 'pwd'
          ? t('admin.login.errorEmptyPassword', 'Please enter password')
          : t('admin.login.errorEmptyCode', 'Please enter verification code'),
    },
  ],
}))

async function handleSubmit(): Promise<void> {
  submitError.value = ''
  try {
    // Field-level validation via NForm rules - throws if invalid.
    await formRef.value?.validate()
  } catch {
    // Naive-UI shows inline errors automatically; nothing else to do.
    return
  }

  submitting.value = true
  try {
    await props.onLogin({ ...form, method: method.value })
    emit('success')
  } catch (err) {
    const message =
      err instanceof Error
        ? err.message
        : t('admin.login.errorGeneric', 'Login failed')
    submitError.value = message
    emit('error', message)
  } finally {
    submitting.value = false
  }
}

function fillDemo(account: DemoAccount): void {
  form.userName = account.userName
  form.password = account.password
}
</script>

<template>
  <!--
    Wrapper switching: `page` keeps the legacy full-page wrapper (used when the
    card is consumed without TLoginPage). `standalone` emits only the NCard so
    TLoginPage controls the surrounding background.
  -->
  <component
    :is="variant === 'page' ? 'main' : 'div'"
    :class="
      variant === 'page' ? 't-admin-login t-admin-login--page' : 't-admin-login'
    "
  >
    <div :class="variant === 'page' ? 't-admin-login__container' : 't-admin-login__bare'">
      <NCard class="t-admin-login__card" :bordered="false">
        <template #header>
          <div class="t-admin-login__header">
            <h2 class="t-admin-login__title">{{ title }}</h2>
            <p v-if="subtitle" class="t-admin-login__subtitle">{{ subtitle }}</p>
          </div>
        </template>

        <NTabs
          v-if="enableCodeLogin"
          v-model:value="method"
          type="line"
          justify-content="space-evenly"
          class="t-admin-login__tabs"
        >
          <NTabPane name="pwd" :tab="t('admin.login.pwd', 'Password')" />
          <NTabPane name="code" :tab="t('admin.login.code', 'SMS code')" />
        </NTabs>

        <NForm
          ref="formRef"
          :model="form"
          :rules="rules"
          size="large"
          :show-label="false"
          @submit.prevent="handleSubmit"
        >
          <NFormItem path="userName">
            <NInput
              v-model:value="form.userName"
              :placeholder="t('admin.login.userNamePlaceholder', 'Enter username')"
              autocomplete="username"
              @keyup.enter="handleSubmit"
            />
          </NFormItem>

          <NFormItem v-if="method === 'pwd'" path="password">
            <NInput
              v-model:value="form.password"
              type="password"
              show-password-on="click"
              :placeholder="t('admin.login.passwordPlaceholder', 'Enter password')"
              autocomplete="current-password"
              @keyup.enter="handleSubmit"
            />
          </NFormItem>

          <NFormItem v-else path="password">
            <div class="t-admin-login__code-row">
              <NInput
                v-model:value="form.password"
                :placeholder="t('admin.login.codePlaceholder', 'Enter 6-digit code')"
              />
              <NButton tertiary type="primary">{{ codeBtnLabel }}</NButton>
            </div>
          </NFormItem>

          <div class="t-admin-login__row">
            <NCheckbox v-model:checked="form.remember">
              {{ t('admin.login.rememberMe', 'Remember me') }}
            </NCheckbox>
            <a
              v-if="$slots['forgot-link']"
              class="t-admin-login__forgot"
              href="#"
              @click.prevent
            >
              <slot name="forgot-link" />
            </a>
          </div>

          <NButton
            type="primary"
            block
            round
            size="large"
            :loading="submitting"
            attr-type="submit"
            class="t-admin-login__submit"
            @click="handleSubmit"
          >
            {{ submitLabel }}
          </NButton>

          <p v-if="submitError" class="t-admin-login__error">{{ submitError }}</p>
        </NForm>

        <!-- Demo account quick-fill cards -->
        <div v-if="demoAccounts.length > 0" class="t-admin-login__demos">
          <p class="t-admin-login__demos-title">
            {{ t('admin.login.demoAccounts', 'Demo accounts') }}
          </p>
          <div class="t-admin-login__demo-grid">
            <button
              v-for="account in demoAccounts"
              :key="account.userName"
              type="button"
              class="t-admin-login__demo"
              @click="fillDemo(account)"
            >
              <span class="t-admin-login__demo-label">{{ account.label }}</span>
              <span v-if="account.description" class="t-admin-login__demo-desc">
                {{ account.description }}
              </span>
            </button>
          </div>
        </div>

        <!-- Third-party login slot -->
        <template v-if="$slots['third-party']">
          <NDivider>
            <span class="t-admin-login__divider-text">
              {{ t('admin.login.orLoginWith', 'Or continue with') }}
            </span>
          </NDivider>
          <div class="t-admin-login__third-party">
            <slot name="third-party" />
          </div>
        </template>

        <!-- Footer slot (signup link, etc) -->
        <template v-if="$slots['footer']" #footer>
          <slot name="footer" />
        </template>
      </NCard>
    </div>
  </component>
</template>

<style scoped>
.t-admin-login--page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  background: var(--tnzi-layout-bg, linear-gradient(135deg, #f5f7fa 0%, #e4ecf7 100%));
  padding: 24px;
}
.t-admin-login__container {
  display: flex;
  align-items: stretch;
  gap: 48px;
  max-width: 920px;
  width: 100%;
}
.t-admin-login__bare {
  width: 100%;
}
.t-admin-login__card {
  width: 100%;
  max-width: 92vw;
  background: rgb(var(--tnzi-container-bg-rgb, 255 255 255) / 1);
  border-radius: 12px;
  box-shadow:
    0 12px 32px rgb(0 21 41 / 0.08),
    0 4px 12px rgb(0 21 41 / 0.04);
}
.t-admin-login--page .t-admin-login__card {
  flex: 0 0 420px;
}
.t-admin-login__header {
  text-align: center;
  padding: 8px 0 0;
}
.t-admin-login__title {
  margin: 0;
  font-size: 22px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-admin-login__subtitle {
  margin: 8px 0 0;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 18px;
  font-weight: 500;
  line-height: 1.5;
}
.t-admin-login__tabs {
  margin-bottom: 4px;
}
.t-admin-login__code-row {
  display: flex;
  gap: 8px;
  width: 100%;
}
.t-admin-login__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: 4px 0 16px;
}
.t-admin-login__forgot {
  color: var(--tnzi-primary);
  font-size: 13px;
  text-decoration: none;
}
.t-admin-login__forgot:hover {
  text-decoration: underline;
}
.t-admin-login__submit {
  height: 44px;
  font-size: 15px;
  font-weight: 500;
  margin-top: 4px;
}
.t-admin-login__error {
  margin: 12px 0 0;
  font-size: 13px;
  color: var(--tnzi-error);
  text-align: center;
}
.t-admin-login__demos {
  margin-top: 24px;
  padding-top: 16px;
  border-top: 1px dashed var(--tnzi-border);
}
.t-admin-login__demos-title {
  margin: 0 0 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.t-admin-login__demo-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
  gap: 8px;
}
.t-admin-login__demo {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 10px;
  background: var(--tnzi-layout-bg, #f5f7fa);
  border: 1px solid var(--tnzi-border, #e5e7eb);
  border-radius: 6px;
  cursor: pointer;
  text-align: left;
  transition: border-color 0.15s, background 0.15s;
}
.t-admin-login__demo:hover {
  border-color: var(--tnzi-primary);
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.06);
}
.t-admin-login__demo-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-admin-login__demo-desc {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-admin-login__divider-text {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-admin-login__third-party {
  display: flex;
  justify-content: center;
  gap: 12px;
}
@media (max-width: 768px) {
  .t-admin-login__container {
    max-width: 480px;
  }
}
</style>
