<template>
  <div :style="outerStyle">
    <div class="t-centered-layout__inner" :style="innerStyle">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, type CSSProperties } from 'vue'

interface Props {
  /** Max width of the inner container. Defaults to unlimited. */
  maxWidth?: string
  /** Padding around the content. */
  padding?: string
}

const props = withDefaults(defineProps<Props>(), {
  maxWidth: '100%',
  padding: '24px',
})

const outerStyle = computed<CSSProperties>(() => ({
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  minHeight: '100vh',
  padding: props.padding,
  backgroundColor: 'var(--tnzi-layout-bg)',
}))

const innerStyle = computed<CSSProperties>(() => ({
  width: '100%',
  maxWidth: props.maxWidth,
}))
</script>
