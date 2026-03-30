<template>
  <n-form
    ref="formRef"
    :model="formModel"
    :rules="rules"
    label-placement="top"
    :disabled="props.disabled"
  >
    <!-- 邮箱 -->
    <n-form-item :label="props.emailLabel ?? 'Email'" path="email">
      <n-input
        v-model:value="formModel.email"
        placeholder="Enter your registered email"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 验证码 -->
    <n-form-item :label="props.codeLabel ?? 'Verification Code'" path="code">
      <n-space :wrap="false" style="width: 100%">
        <n-input
          v-model:value="formModel.code"
          placeholder="Enter verification code"
          style="flex: 1"
          @keydown.enter="handleSubmit"
        />
        <n-button
          :disabled="sendCodeDisabled"
          @click="handleSendCode"
        >
          {{ sendCodeDisabled ? `${countdown}s` : (props.sendCodeLabel ?? 'Send Code') }}
        </n-button>
      </n-space>
    </n-form-item>

    <!-- 新密码 -->
    <n-form-item :label="props.passwordLabel ?? 'New Password'" path="password">
      <n-input
        v-model:value="formModel.password"
        type="password"
        show-password-on="click"
        placeholder="Enter new password"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 确认密码 -->
    <n-form-item :label="props.confirmPasswordLabel ?? 'Confirm Password'" path="confirmPassword">
      <n-input
        v-model:value="formModel.confirmPassword"
        type="password"
        show-password-on="click"
        placeholder="Re-enter new password"
        @keydown.enter="handleSubmit"
      />
    </n-form-item>

    <!-- 按钮 -->
    <n-form-item :show-label="false">
      <n-space style="width: 100%">
        <n-button
          type="primary"
          :loading="props.loading"
          :disabled="props.disabled"
          @click="handleSubmit"
        >
          {{ props.submitLabel ?? 'Reset Password' }}
        </n-button>
        <n-button @click="emit('cancel')">
          {{ props.cancelLabel ?? 'Cancel' }}
        </n-button>
      </n-space>
    </n-form-item>
  </n-form>
</template>

<script setup lang="ts">
// TODO: Replace hardcoded strings with useI18n() translations
import { ref, onUnmounted } from 'vue'
import {
  NForm,
  NFormItem,
  NInput,
  NButton,
  NSpace,
  type FormRules,
  type FormInst
} from 'naive-ui'
import type { IPasswordResetProps } from '@tnzi/core'

const props = withDefaults(defineProps<IPasswordResetProps>(), {
  loading: false,
  disabled: false,
  countdownSeconds: 60,
})

// 事件定义 (matches IPasswordResetEmits from core)
const emit = defineEmits<{
  submit: [data: { email: string; code: string; password: string }]
  cancel: []
  sendCode: [email: string]
}>()

// 表单引用和模型
const formRef = ref<FormInst | null>(null)

const formModel = ref({
  email: '',
  code: '',
  password: '',
  confirmPassword: '',
})

// 验证码倒计时
const countdown = ref(0)
const sendCodeDisabled = ref(false)
let countdownTimer: ReturnType<typeof setInterval> | null = null

function handleSendCode(): void {
  if (!formModel.value.email) return
  emit('sendCode', formModel.value.email)
  sendCodeDisabled.value = true
  countdown.value = props.countdownSeconds ?? 60
  countdownTimer = setInterval(() => {
    countdown.value--
    if (countdown.value <= 0) {
      sendCodeDisabled.value = false
      if (countdownTimer) {
        clearInterval(countdownTimer)
        countdownTimer = null
      }
    }
  }, 1000)
}

onUnmounted(() => {
  if (countdownTimer) {
    clearInterval(countdownTimer)
    countdownTimer = null
  }
})

// 验证规则
const rules: FormRules = {
  email: [
    { required: true, message: 'Please enter email', trigger: ['input', 'blur'] },
    { type: 'email', message: 'Please enter a valid email address', trigger: ['input', 'blur'] }
  ],
  code: [
    { required: true, message: 'Please enter verification code', trigger: ['input', 'blur'] }
  ],
  password: [
    { required: true, message: 'Please enter new password', trigger: ['input', 'blur'] },
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
    code: formModel.value.code,
    password: formModel.value.password,
  })
}
</script>
