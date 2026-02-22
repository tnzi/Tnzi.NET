<script setup lang="ts">
import { computed } from 'vue'
import {
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NSelect,
  NCheckbox,
  NRadioGroup,
  NRadio,
  NDatePicker,
  NSwitch,
  NSpace,
  NButton,
  NGrid,
  NGi,
} from 'naive-ui'
import type { FormRules, FormItemRule } from 'naive-ui'
import type { IDynamicFormField, IFormRule } from '@tnzi/core'

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
  submit: [data: Record<string, unknown>]
  reset: []
  fieldChange: [key: string, value: unknown]
}>()

/**
 * 将核心 IFormRule 转换为 naive-ui FormItemRule
 */
function convertRule(rule: IFormRule): FormItemRule {
  const naiveRule: FormItemRule = {}

  if (rule.required !== undefined) naiveRule.required = rule.required
  if (rule.message !== undefined) naiveRule.message = rule.message
  if (rule.trigger !== undefined) naiveRule.trigger = rule.trigger
  if (rule.min !== undefined) naiveRule.min = rule.min
  if (rule.max !== undefined) naiveRule.max = rule.max
  if (rule.pattern !== undefined) naiveRule.pattern = rule.pattern
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
}

/**
 * 根据字段定义自动生成验证规则
 */
const naiveRules = computed<FormRules>(() => {
  const rules: FormRules = {}

  for (const field of props.fields) {
    const fieldRules: FormItemRule[] = []

    // 根据 required 属性自动生成规则
    if (field.required) {
      fieldRules.push({
        required: true,
        message: `${field.label || field.key} is required`,
        trigger: ['blur', 'change'],
      })
    }

    // 转换自定义规则
    if (field.rules) {
      for (const rule of field.rules) {
        fieldRules.push(convertRule(rule))
      }
    }

    if (fieldRules.length > 0) {
      rules[field.key] = fieldRules
    }
  }

  return rules
})

/**
 * 处理字段值变更
 */
function handleFieldChange(key: string, value: unknown): void {
  props.model[key] = value
  emit('fieldChange', key, value)
}

function handleSubmit(): void {
  emit('submit', props.model)
}

function handleReset(): void {
  // 重置所有字段为初始值
  for (const field of props.fields) {
    if (field.type === 'checkbox' || field.type === 'switch') {
      props.model[field.key] = false
    } else if (field.type === 'number') {
      props.model[field.key] = null
    } else {
      props.model[field.key] = null
    }
  }
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
          <!-- text / email -->
          <NInput
            v-if="field.type === 'text' || field.type === 'email'"
            :value="model[field.key]"
            :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- password -->
          <NInput
            v-else-if="field.type === 'password'"
            :value="model[field.key]"
            type="password"
            show-password-on="click"
            :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- number -->
          <NInputNumber
            v-else-if="field.type === 'number'"
            :value="model[field.key]"
            :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
            :disabled="field.disabled"
            :min="field.min"
            :max="field.max"
            style="width: 100%"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- textarea -->
          <NInput
            v-else-if="field.type === 'textarea'"
            :value="model[field.key]"
            type="textarea"
            :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- select -->
          <NSelect
            v-else-if="field.type === 'select'"
            :value="model[field.key]"
            :options="field.options"
            :placeholder="field.placeholder || `Select ${field.label || field.key}`"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- radio -->
          <NRadioGroup
            v-else-if="field.type === 'radio'"
            :value="model[field.key]"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          >
            <NSpace>
              <NRadio
                v-for="option in field.options"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </NRadio>
            </NSpace>
          </NRadioGroup>

          <!-- checkbox -->
          <NCheckbox
            v-else-if="field.type === 'checkbox'"
            :checked="model[field.key]"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:checked="handleFieldChange(field.key, $event)"
          />

          <!-- switch -->
          <NSwitch
            v-else-if="field.type === 'switch'"
            :value="model[field.key]"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- date -->
          <NDatePicker
            v-else-if="field.type === 'date'"
            :value="model[field.key]"
            type="date"
            :placeholder="field.placeholder || `Select date`"
            :disabled="field.disabled"
            style="width: 100%"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- datetime -->
          <NDatePicker
            v-else-if="field.type === 'datetime'"
            :value="model[field.key]"
            type="datetime"
            :placeholder="field.placeholder || `Select date and time`"
            :disabled="field.disabled"
            style="width: 100%"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
          />

          <!-- file (use NInput as fallback) -->
          <NInput
            v-else-if="field.type === 'file'"
            :value="model[field.key]"
            :placeholder="field.placeholder || `Select file`"
            :disabled="field.disabled"
            v-bind="field.props"
            @update:value="handleFieldChange(field.key, $event)"
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
        <!-- text / email -->
        <NInput
          v-if="field.type === 'text' || field.type === 'email'"
          :value="model[field.key]"
          :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- password -->
        <NInput
          v-else-if="field.type === 'password'"
          :value="model[field.key]"
          type="password"
          show-password-on="click"
          :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- number -->
        <NInputNumber
          v-else-if="field.type === 'number'"
          :value="model[field.key]"
          :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
          :disabled="field.disabled"
          :min="field.min"
          :max="field.max"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- textarea -->
        <NInput
          v-else-if="field.type === 'textarea'"
          :value="model[field.key]"
          type="textarea"
          :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- select -->
        <NSelect
          v-else-if="field.type === 'select'"
          :value="model[field.key]"
          :options="field.options"
          :placeholder="field.placeholder || `Select ${field.label || field.key}`"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- radio -->
        <NRadioGroup
          v-else-if="field.type === 'radio'"
          :value="model[field.key]"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        >
          <NSpace>
            <NRadio
              v-for="option in field.options"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </NRadio>
          </NSpace>
        </NRadioGroup>

        <!-- checkbox -->
        <NCheckbox
          v-else-if="field.type === 'checkbox'"
          :checked="model[field.key]"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:checked="handleFieldChange(field.key, $event)"
        />

        <!-- switch -->
        <NSwitch
          v-else-if="field.type === 'switch'"
          :value="model[field.key]"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- date -->
        <NDatePicker
          v-else-if="field.type === 'date'"
          :value="model[field.key]"
          type="date"
          :placeholder="field.placeholder || `Select date`"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- datetime -->
        <NDatePicker
          v-else-if="field.type === 'datetime'"
          :value="model[field.key]"
          type="datetime"
          :placeholder="field.placeholder || `Select date and time`"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />

        <!-- file -->
        <NInput
          v-else-if="field.type === 'file'"
          :value="model[field.key]"
          :placeholder="field.placeholder || `Select file`"
          :disabled="field.disabled"
          v-bind="field.props"
          @update:value="handleFieldChange(field.key, $event)"
        />
      </NFormItem>
    </template>

    <slot name="actions">
      <NSpace justify="end" style="width: 100%">
        <NButton @click="handleReset">Reset</NButton>
        <NButton type="primary" @click="handleSubmit">Submit</NButton>
      </NSpace>
    </slot>
  </NForm>
</template>
