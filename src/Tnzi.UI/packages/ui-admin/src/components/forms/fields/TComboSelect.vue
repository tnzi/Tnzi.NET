<template>
  <!--
    "Type or pick" combobox - an `NSelect` in `filterable` + `tag` mode, so the
    user chooses a suggestion OR types a free-text value (no explicit "Other"
    entry needed). The bound value is ALWAYS a string; a hand-typed value stays
    displayable even when it isn't in the suggestion list. Use for free-text
    string fields with common presets (injury type, referral source, …).
  -->
  <n-select
    :value="modelValue ?? null"
    :options="merged"
    filterable
    tag
    :clearable="clearable"
    :placeholder="placeholder"
    :disabled="disabled"
    @update:value="(v) => emit('update:modelValue', (v as string | null) ?? null)"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NSelect, type SelectOption } from 'naive-ui'

interface Props {
  modelValue?: string | null
  /** Suggestions: a plain string list or `{ label, value }` objects. */
  options?: Array<string | { label: string; value: string }>
  placeholder?: string
  disabled?: boolean
  /** Default true. */
  clearable?: boolean
}

const props = withDefaults(defineProps<Props>(), { clearable: true })
const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()

const normalized = computed<SelectOption[]>(() =>
  (props.options ?? []).map((o) => (typeof o === 'string' ? { label: o, value: o } : o)),
)

// Keep a hand-typed value selectable/visible even though it is not a suggestion.
const merged = computed<SelectOption[]>(() => {
  const base = normalized.value
  const v = props.modelValue
  if (v != null && v !== '' && !base.some((o) => o.value === v)) {
    return [{ label: v, value: v }, ...base]
  }
  return base
})
</script>
