<template>
  <TField :label="label" :error="error" :hint="hint">
    <n-select
      :value="modelValue"
      :options="options"
      :placeholder="placeholder"
      :disabled="disabled"
      :multiple="multiple"
      :clearable="clearable"
      :filterable="filterable"
      @update:value="emit('update:modelValue', $event)"
    />
  </TField>
</template>

<script setup lang="ts">
import { NSelect, type SelectOption } from 'naive-ui'
import TField from './TField.vue'

type Option = SelectOption & {
  value: string | number
  label: string
  disabled?: boolean
}

interface Props {
  modelValue?: string | number | Array<string | number> | null
  options: Option[]
  label?: string
  placeholder?: string
  error?: string
  hint?: string
  disabled?: boolean
  multiple?: boolean
  clearable?: boolean
  filterable?: boolean
}

defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: string | number | Array<string | number> | null]
}>()
</script>
