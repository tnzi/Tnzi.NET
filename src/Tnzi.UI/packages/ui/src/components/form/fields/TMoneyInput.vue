<template>
  <!--
    Money input primitive — an `NInputNumber` pre-wired for currency amounts:
    fixed precision (default 2), thousand-separator display, an affix (default
    `$`), no spinner buttons, clearable. Replaces the ~15-per-app hand-configured
    `NInputNumber + format/parse + $/CAD affixes` blocks. Bare (no field wrapper)
    so it drops into forms, table cells, and schema fields alike.
  -->
  <n-input-number
    :value="modelValue ?? null"
    :placeholder="placeholder"
    :disabled="disabled"
    :min="min"
    :max="max"
    :precision="precision"
    :show-button="showButton"
    :clearable="clearable"
    :format="format"
    :parse="parse"
    class="t-money-input"
    @update:value="(v) => emit('update:modelValue', v)"
  >
    <template v-if="prefix" #prefix>{{ prefix }}</template>
    <template v-if="suffix" #suffix>{{ suffix }}</template>
  </n-input-number>
</template>

<script setup lang="ts">
import { NInputNumber } from 'naive-ui'

interface Props {
  modelValue?: number | null
  placeholder?: string
  disabled?: boolean
  min?: number
  max?: number
  /** Decimal places shown/enforced. Default 2 (currency). */
  precision?: number
  /** Left affix, e.g. `$`. Default `$`; pass `''` to drop it. */
  prefix?: string
  /** Right affix, e.g. `CAD`. */
  suffix?: string
  /** Show the +/- spinner buttons. Default false. */
  showButton?: boolean
  /** Group the integer part with thousand separators in the display. Default true. */
  thousands?: boolean
  clearable?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  precision: 2,
  prefix: '$',
  showButton: false,
  thousands: true,
  clearable: false,
})

const emit = defineEmits<{ 'update:modelValue': [value: number | null] }>()

function format(value: number | null): string {
  if (value === null || value === undefined || Number.isNaN(value)) return ''
  if (!props.thousands) return value.toFixed(props.precision)
  return value.toLocaleString('en-US', {
    minimumFractionDigits: props.precision,
    maximumFractionDigits: props.precision,
  })
}

function parse(input: string): number | null {
  const cleaned = input.replace(/,/g, '').trim()
  if (cleaned === '' || cleaned === '-' || cleaned === '.') return null
  const n = Number(cleaned)
  return Number.isNaN(n) ? null : n
}
</script>
