<script setup lang="ts">
import { computed } from 'vue'
import {
  NInput,
  NInputNumber,
  NSelect,
  NCheckbox,
  NRadioGroup,
  NRadio,
  NDatePicker,
  NSwitch,
  NSpace,
} from 'naive-ui'
import type { IDynamicFormField } from '@tnzi/core'

interface Props {
  field: IDynamicFormField
  value: unknown
}

const props = defineProps<Props>()

const emit = defineEmits<{
  change: [value: unknown]
}>()

function handleChange(value: unknown): void {
  emit('change', value)
}

// 类型安全的值计算属性 — 将 unknown 窄化为各组件所需类型
const stringValue = computed((): string | null =>
  typeof props.value === 'string' ? props.value : null,
)

const numberValue = computed((): number | null =>
  typeof props.value === 'number' ? props.value : null,
)

const booleanValue = computed((): boolean =>
  typeof props.value === 'boolean' ? props.value : false,
)

const selectValue = computed((): string | number | null => {
  if (typeof props.value === 'string' || typeof props.value === 'number') return props.value
  return null
})

const radioValue = computed((): string | number | boolean | null => {
  if (typeof props.value === 'string' || typeof props.value === 'number' || typeof props.value === 'boolean') return props.value
  return null
})

const dateValue = computed((): number | null =>
  typeof props.value === 'number' ? props.value : null,
)
</script>

<template>
  <!-- text / email -->
  <NInput
    v-if="field.type === 'text' || field.type === 'email'"
    :value="stringValue"
    :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- password -->
  <NInput
    v-else-if="field.type === 'password'"
    :value="stringValue"
    type="password"
    show-password-on="click"
    :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- number -->
  <NInputNumber
    v-else-if="field.type === 'number'"
    :value="numberValue"
    :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
    :disabled="field.disabled"
    :min="field.min"
    :max="field.max"
    class="w-full"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- textarea -->
  <NInput
    v-else-if="field.type === 'textarea'"
    :value="stringValue"
    type="textarea"
    :placeholder="field.placeholder || `Enter ${field.label || field.key}`"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- select -->
  <NSelect
    v-else-if="field.type === 'select'"
    :value="selectValue"
    :options="field.options"
    :placeholder="field.placeholder || `Select ${field.label || field.key}`"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- radio -->
  <NRadioGroup
    v-else-if="field.type === 'radio'"
    :value="radioValue"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:value="handleChange"
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
    :checked="booleanValue"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:checked="handleChange"
  />

  <!-- switch -->
  <NSwitch
    v-else-if="field.type === 'switch'"
    :value="booleanValue"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- date -->
  <NDatePicker
    v-else-if="field.type === 'date'"
    :value="dateValue"
    type="date"
    :placeholder="field.placeholder || `Select date`"
    :disabled="field.disabled"
    class="w-full"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- datetime -->
  <NDatePicker
    v-else-if="field.type === 'datetime'"
    :value="dateValue"
    type="datetime"
    :placeholder="field.placeholder || `Select date and time`"
    :disabled="field.disabled"
    class="w-full"
    v-bind="field.props"
    @update:value="handleChange"
  />

  <!-- file (use NInput as fallback) -->
  <NInput
    v-else-if="field.type === 'file'"
    :value="stringValue"
    :placeholder="field.placeholder || `Select file`"
    :disabled="field.disabled"
    v-bind="field.props"
    @update:value="handleChange"
  />
</template>
