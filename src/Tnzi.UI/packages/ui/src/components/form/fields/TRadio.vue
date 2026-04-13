<template>
  <TField :label="label" :error="error" :hint="hint">
    <n-radio-group :value="modelValue" :disabled="disabled" @update:value="emit('update:modelValue', $event)">
      <n-radio
        v-for="opt in options"
        :key="String(opt.value)"
        :value="opt.value"
        :disabled="opt.disabled"
      >
        {{ opt.label }}
      </n-radio>
    </n-radio-group>
  </TField>
</template>

<script setup lang="ts">
import { NRadioGroup, NRadio } from 'naive-ui'
import TField from './TField.vue'

interface Option {
  value: string | number | boolean
  label: string
  disabled?: boolean
}

interface Props {
  modelValue?: string | number | boolean
  options: Option[]
  label?: string
  error?: string
  hint?: string
  disabled?: boolean
}

defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: string | number | boolean]
}>()
</script>
