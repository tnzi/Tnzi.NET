<template>
  <TField :label="label" :error="error" :hint="hint">
    <n-checkbox
      :checked="modelValue"
      :disabled="disabled"
      :indeterminate="indeterminate"
      @update:checked="emit('update:modelValue', $event)"
    >
      <slot>{{ checkboxLabel }}</slot>
    </n-checkbox>
  </TField>
</template>

<script setup lang="ts">
import { NCheckbox } from 'naive-ui'
import TField from './TField.vue'

interface Props {
  modelValue?: boolean
  label?: string
  checkboxLabel?: string
  error?: string
  hint?: string
  disabled?: boolean
  indeterminate?: boolean
}

withDefaults(defineProps<Props>(), {
  modelValue: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()
</script>
