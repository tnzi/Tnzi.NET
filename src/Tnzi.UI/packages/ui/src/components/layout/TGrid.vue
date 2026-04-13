<template>
  <div :style="gridStyle">
    <slot />
  </div>
</template>

<script setup lang="ts">
import { computed, type CSSProperties } from 'vue'

interface Props {
  /** Number of columns. For responsive grids, use UnoCSS utility classes on the wrapping element, e.g. class="md:grid-cols-2 lg:grid-cols-3". */
  cols?: number
  /** Gap between grid items. */
  gap?: string
}

const props = withDefaults(defineProps<Props>(), {
  cols: 12,
  gap: '16px',
})

const gridStyle = computed<CSSProperties>(() => ({
  display: 'grid',
  gridTemplateColumns: `repeat(${props.cols}, minmax(0, 1fr))`,
  gap: props.gap,
}))
</script>
