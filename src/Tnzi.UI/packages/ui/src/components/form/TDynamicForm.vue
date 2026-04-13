<script setup lang="ts">
import { computed } from 'vue'
import {
  NForm,
  NFormItem,
  NSpace,
  NButton,
  NGrid,
  NGi,
} from 'naive-ui'
import type { FormRules, FormItemRule } from 'naive-ui'
import type { IDynamicFormField } from '@tnzi/core'
import { convertFormRule } from '../../utils/naive-helpers'
import DynamicField from './DynamicField.vue'

interface Props {
  model: Record<string, unknown>
  fields: IDynamicFormField[]
  inline?: boolean
  disabled?: boolean
  size?: 'small' | 'medium' | 'large'
}

const props = withDefaults(defineProps<Props>(), {
  inline: false,
  disabled: false,
  size: 'medium',
})

const emit = defineEmits<{
  'update:model': [model: Record<string, unknown>]
  submit: [data: Record<string, unknown>]
  reset: []
  fieldChange: [key: string, value: unknown]
}>()

/**
 * 根据字段定义自动生成验证规则
 */
const naiveRules = computed<FormRules>(() => {
  const rules: FormRules = {}

  for (const field of props.fields) {
    const fieldRules: FormItemRule[] = []

    if (field.required) {
      fieldRules.push({
        required: true,
        message: `${field.label || field.key} is required`,
        trigger: ['blur', 'change'],
      })
    }

    if (field.rules) {
      for (const rule of field.rules) {
        fieldRules.push(convertFormRule(rule))
      }
    }

    if (fieldRules.length > 0) {
      rules[field.key] = fieldRules
    }
  }

  return rules
})

/**
 * 处理字段值变更 — 不直接修改 props，通过 emit 通知父组件
 */
function handleFieldChange(key: string, value: unknown): void {
  const newModel = { ...props.model, [key]: value }
  emit('update:model', newModel)
  emit('fieldChange', key, value)
}

function handleSubmit(): void {
  emit('submit', props.model)
}

function handleReset(): void {
  const resetModel: Record<string, unknown> = {}
  props.fields.forEach(field => {
    resetModel[field.key] = field.type === 'switch' || field.type === 'checkbox' ? false : null
  })
  emit('update:model', resetModel)
  emit('reset')
}

/**
 * 获取字段占用的列数
 */
function getFieldSpan(field: IDynamicFormField): number {
  if (field.type === 'textarea') return 24
  return props.inline ? 12 : 24
}
</script>

<template>
  <NForm
    :model="model"
    :rules="naiveRules"
    :disabled="disabled"
    :size="size"
    :inline="inline"
    label-placement="top"
  >
    <NGrid v-if="!inline" :cols="24" :x-gap="16">
      <NGi v-for="field in fields" :key="field.key" :span="getFieldSpan(field)">
        <NFormItem :label="field.label" :path="field.key">
          <DynamicField
            :field="field"
            :value="model[field.key]"
            @change="handleFieldChange(field.key, $event)"
          />
        </NFormItem>
      </NGi>
    </NGrid>

    <!-- inline 模式 -->
    <template v-else>
      <NFormItem
        v-for="field in fields"
        :key="field.key"
        :label="field.label"
        :path="field.key"
      >
        <DynamicField
          :field="field"
          :value="model[field.key]"
          @change="handleFieldChange(field.key, $event)"
        />
      </NFormItem>
    </template>

    <slot name="actions">
      <NSpace justify="end" class="w-full">
        <NButton @click="handleReset">Reset</NButton>
        <NButton type="primary" @click="handleSubmit">Submit</NButton>
      </NSpace>
    </slot>
  </NForm>
</template>
