<template>
  <n-form
    ref="formRef"
    :model="formModel"
    :rules="rules"
    label-placement="top"
    :disabled="disabled"
  >
    <!-- 用户名 -->
    <n-form-item :label="usernameLabel" path="userName">
      <n-input
        v-model:value="formModel.userName"
        :placeholder="usernamePlaceholder"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 密码 -->
    <n-form-item :label="passwordLabel" path="password">
      <n-input
        v-model:value="formModel.password"
        type="password"
        show-password-on="click"
        :placeholder="passwordPlaceholder"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 验证码 -->
    <n-form-item v-if="showCaptcha" :label="captchaLabel" path="captchaCode">
      <n-space :wrap="false" style="width: 100%">
        <n-input
          v-model:value="formModel.captchaCode"
          :placeholder="captchaPlaceholder"
          style="flex: 1"
          @keydown.enter="handleSubmit"
        />
        <img
          v-if="captchaUrl"
          :src="captchaUrl"
          class="captcha-image"
          alt="captcha"
          @click="onRefreshCaptcha?.()"
        />
      </n-space>
    </n-form-item>

    <!-- 记住我 & 忘记密码 -->
    <div
      v-if="showRememberMe || showForgotPassword"
      class="login-options"
    >
      <n-checkbox
        v-if="showRememberMe"
        v-model:checked="formModel.rememberMe"
      >
        Remember me
      </n-checkbox>
      <n-button
        v-if="showForgotPassword"
        text
        type="primary"
        @click="emit('forgotPassword')"
      >
        Forgot password?
      </n-button>
    </div>

    <!-- 提交按钮 -->
    <n-form-item :show-label="false">
      <n-button
        type="primary"
        block
        :loading="loading"
        :disabled="disabled"
        @click="handleSubmit"
      >
        {{ submitLabel }}
      </n-button>
    </n-form-item>

    <!-- 社交登录 -->
    <template v-if="showSocialLogin && socialProviders && socialProviders.length > 0">
      <n-divider>Or login with</n-divider>
      <n-space justify="center">
        <n-button
          v-for="provider in socialProviders"
          :key="provider"
          secondary
          @click="emit('socialLogin', provider)"
        >
          {{ provider }}
        </n-button>
      </n-space>
    </template>
  </n-form>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import {
  NForm,
  NFormItem,
  NInput,
  NCheckbox,
  NButton,
  NSpace,
  NDivider,
  type FormRules,
  type FormInst
} from 'naive-ui'

// Props 定义
interface Props {
  showRememberMe?: boolean
  showForgotPassword?: boolean
  showSocialLogin?: boolean
  socialProviders?: ('Google' | 'Microsoft' | 'Facebook' | 'Twitter' | 'GitHub')[]
  loading?: boolean
  disabled?: boolean
  usernameLabel?: string
  passwordLabel?: string
  submitLabel?: string
  usernamePlaceholder?: string
  passwordPlaceholder?: string
  showCaptcha?: boolean
  captchaId?: string
  captchaUrl?: string
  onRefreshCaptcha?: () => void
  captchaLabel?: string
  captchaPlaceholder?: string
}

withDefaults(defineProps<Props>(), {
  showRememberMe: true,
  showForgotPassword: true,
  showSocialLogin: false,
  socialProviders: () => [],
  loading: false,
  disabled: false,
  usernameLabel: 'Username',
  passwordLabel: 'Password',
  submitLabel: 'Login',
  usernamePlaceholder: 'Enter username',
  passwordPlaceholder: 'Enter password',
  showCaptcha: false,
  captchaId: undefined,
  captchaUrl: undefined,
  onRefreshCaptcha: undefined,
  captchaLabel: 'Captcha',
  captchaPlaceholder: 'Enter captcha'
})

// 事件定义
const emit = defineEmits<{
  submit: [credentials: {
    userName: string
    password: string
    rememberMe?: boolean
    captchaId?: string
    captchaCode?: string
  }]
  forgotPassword: []
  socialLogin: [provider: string]
}>()

// 表单引用和模型
const formRef = ref<FormInst | null>(null)

const formModel = ref({
  userName: '',
  password: '',
  rememberMe: false,
  captchaCode: ''
})

// 验证规则
const rules: FormRules = {
  userName: [
    { required: true, message: 'Please enter username', trigger: ['input', 'blur'] }
  ],
  password: [
    { required: true, message: 'Please enter password', trigger: ['input', 'blur'] },
    { min: 6, message: 'Password must be at least 6 characters', trigger: ['input', 'blur'] }
  ],
  captchaCode: [
    { required: true, message: 'Please enter captcha', trigger: ['input', 'blur'] }
  ]
}

// 提交处理
async function handleSubmit() {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }

  emit('submit', {
    userName: formModel.value.userName,
    password: formModel.value.password,
    rememberMe: formModel.value.rememberMe,
    captchaId: undefined,
    captchaCode: formModel.value.captchaCode || undefined
  })
}
</script>

<style scoped>
.login-options {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.captcha-image {
  height: 34px;
  cursor: pointer;
  border-radius: 3px;
  flex-shrink: 0;
}
</style>
