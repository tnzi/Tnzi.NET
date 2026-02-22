<template>
  <n-form
    ref="formRef"
    :model="formModel"
    :rules="rules"
    label-placement="top"
    :disabled="disabled"
  >
    <!-- 邮箱 -->
    <n-form-item :label="emailLabel" path="email">
      <n-input
        v-model:value="formModel.email"
        placeholder="Enter email"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 用户名（可选） -->
    <n-form-item v-if="showUsername" :label="usernameLabel" path="userName">
      <n-input
        v-model:value="formModel.userName"
        placeholder="Enter username"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 手机号（可选） -->
    <n-form-item v-if="showPhone" :label="phoneLabel" path="phoneNumber">
      <n-input
        v-model:value="formModel.phoneNumber"
        placeholder="Enter phone number"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 密码 -->
    <n-form-item :label="passwordLabel" path="password">
      <n-input
        v-model:value="formModel.password"
        type="password"
        show-password-on="click"
        placeholder="Enter password"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 确认密码 -->
    <n-form-item :label="confirmPasswordLabel" path="confirmPassword">
      <n-input
        v-model:value="formModel.confirmPassword"
        type="password"
        show-password-on="click"
        placeholder="Re-enter password"
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

    <!-- 同意协议 -->
    <n-form-item :show-label="false" path="agreement">
      <n-checkbox v-model:checked="formModel.agreement">
        I agree to the Terms of Service and Privacy Policy
      </n-checkbox>
    </n-form-item>

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
      <n-divider>Or register with</n-divider>
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

    <!-- 登录链接 -->
    <div v-if="showLoginLink" class="login-link">
      <n-button text type="primary" @click="emit('login')">
        {{ loginLinkText }}
      </n-button>
    </div>
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
  showUsername?: boolean
  showPhone?: boolean
  showSocialLogin?: boolean
  socialProviders?: ('Google' | 'Microsoft' | 'Facebook' | 'Twitter' | 'GitHub')[]
  loading?: boolean
  disabled?: boolean
  emailLabel?: string
  usernameLabel?: string
  phoneLabel?: string
  passwordLabel?: string
  confirmPasswordLabel?: string
  submitLabel?: string
  showLoginLink?: boolean
  loginLinkText?: string
  showCaptcha?: boolean
  captchaId?: string
  captchaUrl?: string
  onRefreshCaptcha?: () => void
  captchaLabel?: string
  captchaPlaceholder?: string
}

withDefaults(defineProps<Props>(), {
  showUsername: true,
  showPhone: false,
  showSocialLogin: false,
  socialProviders: () => [],
  loading: false,
  disabled: false,
  emailLabel: 'Email',
  usernameLabel: 'Username',
  phoneLabel: 'Phone',
  passwordLabel: 'Password',
  confirmPasswordLabel: 'Confirm Password',
  submitLabel: 'Register',
  showLoginLink: true,
  loginLinkText: 'Already have an account? Login',
  showCaptcha: false,
  captchaId: undefined,
  captchaUrl: undefined,
  onRefreshCaptcha: undefined,
  captchaLabel: 'Captcha',
  captchaPlaceholder: 'Enter captcha'
})

// 事件定义
const emit = defineEmits<{
  submit: [data: {
    email: string
    password: string
    userName?: string
    phoneNumber?: string
    captchaId?: string
    captchaCode?: string
  }]
  login: []
  socialLogin: [provider: string]
}>()

// 表单引用和模型
const formRef = ref<FormInst | null>(null)

const formModel = ref({
  email: '',
  userName: '',
  phoneNumber: '',
  password: '',
  confirmPassword: '',
  captchaCode: '',
  agreement: false
})

// 验证规则
const rules: FormRules = {
  email: [
    { required: true, message: 'Please enter email', trigger: ['input', 'blur'] },
    { type: 'email', message: 'Please enter a valid email address', trigger: ['input', 'blur'] }
  ],
  userName: [
    { required: true, message: 'Please enter username', trigger: ['input', 'blur'] }
  ],
  password: [
    { required: true, message: 'Please enter password', trigger: ['input', 'blur'] },
    { min: 6, message: 'Password must be at least 6 characters', trigger: ['input', 'blur'] }
  ],
  confirmPassword: [
    { required: true, message: 'Please confirm password', trigger: ['input', 'blur'] },
    {
      validator(_rule, value: string) {
        if (value !== formModel.value.password) {
          return new Error('Passwords do not match')
        }
        return true
      },
      trigger: ['input', 'blur']
    }
  ],
  captchaCode: [
    { required: true, message: 'Please enter captcha', trigger: ['input', 'blur'] }
  ],
  agreement: [
    {
      validator(_rule, value: boolean) {
        if (!value) {
          return new Error('You must agree to the terms')
        }
        return true
      },
      trigger: 'change'
    }
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
    email: formModel.value.email,
    password: formModel.value.password,
    userName: formModel.value.userName || undefined,
    phoneNumber: formModel.value.phoneNumber || undefined,
    captchaId: undefined,
    captchaCode: formModel.value.captchaCode || undefined
  })
}
</script>

<style scoped>
.login-link {
  text-align: center;
  margin-top: 16px;
}

.captcha-image {
  height: 34px;
  cursor: pointer;
  border-radius: 3px;
  flex-shrink: 0;
}
</style>
