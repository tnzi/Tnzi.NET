<script setup lang="ts">
import { computed } from 'vue'
import { NInput, NButton, NSpace } from 'naive-ui'

interface Props {
  modelValue?: string
  placeholder?: string
  loading?: boolean
  disabled?: boolean
  clearable?: boolean
  showReset?: boolean
  searchLabel?: string
  resetLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: '',
  placeholder: 'Search...',
  loading: false,
  disabled: false,
  clearable: true,
  showReset: true,
  searchLabel: 'Search',
  resetLabel: 'Reset',
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  search: [keyword: string]
  reset: []
}>()

const inputValue = computed({
  get: () => props.modelValue,
  set: (value: string) => emit('update:modelValue', value),
})

function handleSearch(): void {
  emit('search', inputValue.value)
}

function handleReset(): void {
  emit('update:modelValue', '')
  emit('reset')
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter') {
    handleSearch()
  }
}
</script>

<template>
  <NSpace align="center" :wrap="false">
    <NInput
      v-model:value="inputValue"
      :placeholder="placeholder"
      :disabled="disabled"
      :clearable="clearable"
      @keydown="handleKeydown"
    />
    <NButton
      type="primary"
      :loading="loading"
      :disabled="disabled"
      @click="handleSearch"
    >
      {{ searchLabel }}
    </NButton>
    <NButton
      v-if="showReset"
      :disabled="disabled"
      @click="handleReset"
    >
      {{ resetLabel }}
    </NButton>
  </NSpace>
</template>
