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
        :placeholder="emailPlaceholder"
        @keydown.enter="handleSubmit"
      />
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

    <!-- 返回登录链接 -->
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
  NButton,
  type FormRules,
  type FormInst
} from 'naive-ui'

// Props 定义
interface Props {
  loading?: boolean
  disabled?: boolean
  emailLabel?: string
  submitLabel?: string
  emailPlaceholder?: string
  showLoginLink?: boolean
  loginLinkText?: string
}

withDefaults(defineProps<Props>(), {
  loading: false,
  disabled: false,
  emailLabel: 'Email',
  submitLabel: 'Send Reset Link',
  emailPlaceholder: 'Enter your registered email',
  showLoginLink: true,
  loginLinkText: 'Back to Login'
})

// 事件定义
const emit = defineEmits<{
  submit: [data: { email: string }]
  login: []
}>()

// 表单引用和模型
const formRef = ref<FormInst | null>(null)

const formModel = ref({
  email: ''
})

// 验证规则
const rules: FormRules = {
  email: [
    { required: true, message: 'Please enter email', trigger: ['input', 'blur'] },
    { type: 'email', message: 'Please enter a valid email address', trigger: ['input', 'blur'] }
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
    email: formModel.value.email
  })
}
</script>

<style scoped>
.login-link {
  text-align: center;
  margin-top: 16px;
}
</style>
