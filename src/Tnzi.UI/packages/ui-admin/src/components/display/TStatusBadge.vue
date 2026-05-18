<script setup lang="ts">
/**
 * `TStatusBadge` — soybean-style status pill with semantic color mapping.
 *
 * Accepts either a boolean (true=success/false=danger) or a string value
 * mapped via the `mapping` prop. Falls back to neutral color for unknown
 * keys.
 */
import { computed } from 'vue'
import { NTag } from 'naive-ui'

export type StatusType = 'success' | 'info' | 'warning' | 'error' | 'default'

interface Props {
  value: string | number | boolean | null | undefined
  /** Map values to {type, label}. Keys are stringified `value`. */
  mapping?: Record<string, { type: StatusType; label?: string }>
  /** Label override (skips mapping lookup). */
  label?: string
  /** Type override (skips mapping lookup). */
  type?: StatusType
  size?: 'tiny' | 'small' | 'medium' | 'large'
}

const props = withDefaults(defineProps<Props>(), {
  mapping: () => ({}),
  label: undefined,
  type: undefined,
  size: 'small',
})

const DEFAULT_BOOL_MAPPING: Record<string, { type: StatusType; label: string }> = {
  true: { type: 'success', label: 'Enabled' },
  // soybean parity — disabled rows render with the `warning` tone (orange)
  // rather than neutral grey so the negative state is visually obvious.
  false: { type: 'warning', label: 'Disabled' },
}

const resolved = computed<{ type: StatusType; label: string }>(() => {
  // Explicit overrides win
  if (props.type && props.label) return { type: props.type, label: props.label }

  const key = String(props.value)
  const fromMapping = props.mapping[key]
  if (fromMapping) {
    return {
      type: props.type ?? fromMapping.type,
      label: props.label ?? fromMapping.label ?? key,
    }
  }

  // Boolean default mapping
  if (typeof props.value === 'boolean') {
    const entry = DEFAULT_BOOL_MAPPING[key]!
    return { type: props.type ?? entry.type, label: props.label ?? entry.label }
  }

  // Fallback — render whatever we have as neutral
  return {
    type: props.type ?? 'default',
    label: props.label ?? key,
  }
})
</script>

<template>
  <NTag :type="resolved.type === 'default' ? 'default' : resolved.type" :size="size" round>
    {{ resolved.label }}
  </NTag>
</template>
