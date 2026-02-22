<script setup lang="ts">
import { ref, computed } from 'vue'
import { NForm, NSpace, NButton } from 'naive-ui'
import type { FormInst, FormRules, FormItemRule } from 'naive-ui'
import type { IFormRule } from '@tnzi/core'

interface Props {
  model: Record<string, unknown>
  rules?: Record<string, IFormRule[]>
  labelWidth?: number | string
  labelPlacement?: 'left' | 'top'
  disabled?: boolean
  showRequireMark?: boolean
  size?: 'small' | 'medium' | 'large'
}

const props = withDefaults(defineProps<Props>(), {
  labelWidth: 'auto',
  labelPlacement: 'top',
  disabled: false,
  showRequireMark: true,
  size: 'medium',
})

const emit = defineEmits<{
  submit: [data: Record<string, unknown>]
  reset: []
  validateError: [errors: Record<string, string[]>]
}>()

const formRef = ref<FormInst | null>(null)

/**
 * 将核心 IFormRule 转换为 naive-ui FormRules 格式
 */
const naiveRules = computed<FormRules>(() => {
  if (!props.rules) return {}

  const converted: FormRules = {}
  for (const [field, rules] of Object.entries(props.rules)) {
    converted[field] = rules.map((rule): FormItemRule => {
      const naiveRule: FormItemRule = {}

      if (rule.required !== undefined) {
        naiveRule.required = rule.required
      }
      if (rule.message !== undefined) {
        naiveRule.message = rule.message
      }
      if (rule.trigger !== undefined) {
        naiveRule.trigger = rule.trigger
      }
      if (rule.min !== undefined) {
        naiveRule.min = rule.min
      }
      if (rule.max !== undefined) {
        naiveRule.max = rule.max
      }
      if (rule.pattern !== undefined) {
        naiveRule.pattern = rule.pattern
      }
      if (rule.validator !== undefined) {
        const customValidator = rule.validator
        naiveRule.validator = async (_rule: FormItemRule, value: unknown): Promise<void> => {
          const result = await customValidator(value)
          if (result === true) return
          if (result === false) {
            throw new Error(rule.message || 'Validation failed')
          }
          if (typeof result === 'string') {
            throw new Error(result)
          }
        }
      }

      return naiveRule
    })
  }
  return converted
})

/**
 * 验证表单
 */
async function validate(): Promise<boolean> {
  if (!formRef.value) return false

  try {
    await formRef.value.validate()
    return true
  } catch (errors) {
    if (Array.isArray(errors)) {
      const errorMap: Record<string, string[]> = {}
      for (const errorGroup of errors) {
        if (Array.isArray(errorGroup)) {
          for (const error of errorGroup) {
            const field = error.field as string
            if (!errorMap[field]) {
              errorMap[field] = []
            }
            errorMap[field].push(error.message as string)
          }
        }
      }
      emit('validateError', errorMap)
    }
    return false
  }
}

/**
 * 重置表单验证状态
 */
function resetFields(): void {
  formRef.value?.restoreValidation()
  emit('reset')
}

/**
 * 提交表单：先验证再触发 submit 事件
 */
async function submit(): Promise<void> {
  const valid = await validate()
  if (valid) {
    emit('submit', props.model)
  }
}

function handleSubmit(): void {
  submit()
}

function handleReset(): void {
  resetFields()
}

defineExpose({
  validate,
  resetFields,
  submit,
})
</script>

<template>
  <NForm
    ref="formRef"
    :model="model"
    :rules="naiveRules"
    :label-width="labelWidth"
    :label-placement="labelPlacement"
    :disabled="disabled"
    :show-require-mark="showRequireMark"
    :size="size"
  >
    <slot />
    <slot name="actions">
      <NSpace justify="end">
        <NButton @click="handleReset">Reset</NButton>
        <NButton type="primary" @click="handleSubmit">Submit</NButton>
      </NSpace>
    </slot>
  </NForm>
</template>
