<script setup lang="ts">
import { computed } from 'vue'

/**
 * Skeleton loader — placeholder shapes that shimmer while data loads.
 * Bag of shapes you can compose into table rows, card body skeletons,
 * KPI placeholders, list items.
 *
 * Inspired by the shimmer keyframes already shipped in
 * `styles/transition.css` — this component just hangs the right CSS class
 * on an element with the configured dimensions.
 */
interface Props {
  /** Skeleton variant. */
  variant?: 'rect' | 'circle' | 'text' | 'avatar' | 'image'
  /** Width (number → px, string passes through). */
  width?: number | string
  /** Height (number → px, string passes through). */
  height?: number | string
  /** Border radius override. */
  radius?: number | string
  /** Multiple text lines — for variant=text. */
  lines?: number
  /** Disable animation (e.g. when prefers-reduced-motion is on). */
  animated?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'rect',
  width: '',
  height: '',
  radius: '',
  lines: 1,
  animated: true,
})

const resolvedWidth = computed(() => {
  if (props.width) {
    return typeof props.width === 'number' ? `${props.width}px` : props.width
  }
  switch (props.variant) {
    case 'circle':
    case 'avatar':
      return '40px'
    case 'image':
      return '100%'
    default:
      return '100%'
  }
})

const resolvedHeight = computed(() => {
  if (props.height) {
    return typeof props.height === 'number' ? `${props.height}px` : props.height
  }
  switch (props.variant) {
    case 'circle':
    case 'avatar':
      return '40px'
    case 'image':
      return '180px'
    case 'text':
      return '14px'
    default:
      return '16px'
  }
})

const resolvedRadius = computed(() => {
  if (props.radius) {
    return typeof props.radius === 'number' ? `${props.radius}px` : props.radius
  }
  switch (props.variant) {
    case 'circle':
    case 'avatar':
      return '50%'
    case 'text':
      return 'var(--tnzi-admin-radius-sm, 4px)'
    default:
      return 'var(--tnzi-admin-radius-md, 8px)'
  }
})

const styleObj = computed(() => ({
  width: resolvedWidth.value,
  height: resolvedHeight.value,
  borderRadius: resolvedRadius.value,
}))
</script>

<template>
  <div
    v-if="variant !== 'text' || lines <= 1"
    class="t-skeleton"
    :class="{ 't-skeleton--animated': animated, tnzi: animated }"
    :style="styleObj"
    aria-hidden="true"
  />
  <div v-else class="t-skeleton-stack" aria-hidden="true">
    <div
      v-for="i in lines"
      :key="i"
      class="t-skeleton"
      :class="{ 't-skeleton--animated': animated }"
      :style="{
        ...styleObj,
        width: i === lines && lines > 1 ? '70%' : styleObj.width,
      }"
    />
  </div>
</template>

<style scoped>
.t-skeleton {
  display: block;
  background: rgb(var(--tnzi-border-rgb, 230 230 240) / 0.5);
}

.t-skeleton--animated {
  background: linear-gradient(
    90deg,
    rgb(var(--tnzi-border-rgb, 230 230 240) / 0.4) 0%,
    rgb(var(--tnzi-border-rgb, 230 230 240) / 0.75) 50%,
    rgb(var(--tnzi-border-rgb, 230 230 240) / 0.4) 100%
  );
  background-size: 468px 100%;
  animation: t-skeleton-shimmer 1.4s linear infinite;
}

@keyframes t-skeleton-shimmer {
  0% {
    background-position: -468px 0;
  }
  100% {
    background-position: 468px 0;
  }
}

.t-skeleton-stack {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

@media (prefers-reduced-motion: reduce) {
  .t-skeleton--animated {
    animation: none;
  }
}
</style>
