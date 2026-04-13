<template>
  <div :style="containerStyle">
    <slot />
  </div>
</template>

<script setup lang="ts">
import { computed, type CSSProperties } from 'vue'

type SizePreset = 'sm' | 'md' | 'lg' | 'xl' | '2xl' | 'full'

interface Props {
  /**
   * Maximum width. Accepts a CSS length ('960px', '80rem') or a preset:
   *   - 'sm'  → 640px
   *   - 'md'  → 768px
   *   - 'lg'  → 1024px
   *   - 'xl'  → 1280px (default)
   *   - '2xl' → 1536px
   *   - 'full' → no limit
   */
  maxWidth?: string | SizePreset
  /** If true, disables max-width entirely (full viewport width). */
  fluid?: boolean
  /** Padding in CSS shorthand. Defaults to '0 clamp(16px, 4vw, 32px)' — vertical 0, horizontal responsive. */
  padding?: string
}

const props = withDefaults(defineProps<Props>(), {
  maxWidth: 'xl',
  fluid: false,
  padding: '0 clamp(16px, 4vw, 32px)',
})

const presetMap: Record<SizePreset, string> = {
  sm: '640px',
  md: '768px',
  lg: '1024px',
  xl: '1280px',
  '2xl': '1536px',
  full: 'none',
}

const containerStyle = computed<CSSProperties>(() => {
  if (props.fluid) {
    return {
      width: '100%',
      padding: props.padding,
      marginLeft: 'auto',
      marginRight: 'auto',
    }
  }
  const resolved = presetMap[props.maxWidth as SizePreset] ?? props.maxWidth
  return {
    width: '100%',
    maxWidth: resolved === 'none' ? undefined : resolved,
    padding: props.padding,
    marginLeft: 'auto',
    marginRight: 'auto',
  }
})
</script>
