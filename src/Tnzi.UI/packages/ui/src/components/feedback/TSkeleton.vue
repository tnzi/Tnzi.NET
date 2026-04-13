<template>
  <div
    class="t-skeleton"
    :class="[`t-skeleton--${type}`, animated && 't-skeleton--animated']"
    :style="containerStyle"
  >
    <template v-if="type === 'text' && rows > 1">
      <div
        v-for="i in rows"
        :key="i"
        class="t-skeleton__row"
        :style="{ width: i === rows ? '60%' : '100%' }"
      />
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, type CSSProperties } from 'vue'

type SkeletonType = 'text' | 'rect' | 'circle'

interface Props {
  type?: SkeletonType
  width?: string
  height?: string
  rows?: number
  animated?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  type: 'text',
  width: '100%',
  height: '',
  rows: 1,
  animated: true,
})

const containerStyle = computed<CSSProperties>(() => {
  const style: CSSProperties = { width: props.width }
  if (props.height) style.height = props.height
  else if (props.type === 'circle') style.height = props.width
  else if (props.type === 'rect') style.height = '120px'
  else if (props.type === 'text' && props.rows === 1) style.height = '16px'
  return style
})
</script>

<style scoped>
.t-skeleton {
  background-color: var(--tnzi-border);
  border-radius: 4px;
}
.t-skeleton--circle {
  border-radius: 50%;
}
.t-skeleton--rect {
  border-radius: 8px;
}
.t-skeleton--text {
  display: flex;
  flex-direction: column;
  gap: 8px;
  background-color: transparent;
}
.t-skeleton__row {
  height: 16px;
  background-color: var(--tnzi-border);
  border-radius: 4px;
}
.t-skeleton--animated,
.t-skeleton--animated .t-skeleton__row {
  background: linear-gradient(
    90deg,
    var(--tnzi-border) 0%,
    var(--tnzi-primary-50) 50%,
    var(--tnzi-border) 100%
  );
  background-size: 200% 100%;
  animation: t-skeleton-shimmer 1.5s ease-in-out infinite;
}
@keyframes t-skeleton-shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
</style>
