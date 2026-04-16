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
  /**
   * Padding shorthand. Defaults to '0 clamp(16px, 4vw, 32px)' — vertical 0, horizontal responsive.
   * Overridden by `paddingX` / `paddingY` when either is provided.
   */
  padding?: string
  /** Horizontal padding (paddingLeft + paddingRight). Overrides `padding` when set. */
  paddingX?: string
  /** Vertical padding (paddingTop + paddingBottom). Overrides `padding` when set. */
  paddingY?: string
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

const DEFAULT_X = 'clamp(16px, 4vw, 32px)'
const DEFAULT_Y = '0px'

const containerStyle = computed<CSSProperties>(() => {
  const base: CSSProperties = {
    width: '100%',
    marginLeft: 'auto',
    marginRight: 'auto',
  }

  if (!props.fluid) {
    const resolved = presetMap[props.maxWidth as SizePreset] ?? props.maxWidth
    if (resolved !== 'none') base.maxWidth = resolved
  }

  if (props.paddingX != null || props.paddingY != null) {
    const x = props.paddingX ?? DEFAULT_X
    const y = props.paddingY ?? DEFAULT_Y
    base.paddingLeft = x
    base.paddingRight = x
    base.paddingTop = y
    base.paddingBottom = y
  } else {
    base.padding = props.padding
  }

  return base
})

// Exposed for testing — avoids happy-dom CSS parser normalization
// (which drops clamp() and collapses 4-side padding into shorthand).
defineExpose({ containerStyle })
</script>
