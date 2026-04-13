<template>
  <div class="t-watermark">
    <slot />
    <div class="t-watermark__overlay" :style="overlayStyle" aria-hidden="true" />
  </div>
</template>

<script setup lang="ts">
import { computed, type CSSProperties } from 'vue'

interface Props {
  text: string
  fontSize?: number
  color?: string
  opacity?: number
  rotate?: number
  gap?: number
}

const props = withDefaults(defineProps<Props>(), {
  fontSize: 16,
  color: '#000000',
  opacity: 0.15,
  rotate: -20,
  gap: 120,
})

// Note: this component uses canvas to generate a repeating watermark
// background. In test environments (happy-dom) canvas.getContext('2d') may
// return null; in that case overlayStyle falls through to {} and no
// background-image is produced. Real browsers render normally.
const overlayStyle = computed<CSSProperties>(() => {
  if (typeof document === 'undefined') return {}

  const canvas = document.createElement('canvas')
  const ctx = canvas.getContext('2d')
  if (!ctx) return {}

  const size = props.gap
  canvas.width = size
  canvas.height = size

  ctx.clearRect(0, 0, size, size)
  ctx.globalAlpha = props.opacity
  ctx.fillStyle = props.color
  ctx.font = `${props.fontSize}px sans-serif`
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.translate(size / 2, size / 2)
  ctx.rotate((props.rotate * Math.PI) / 180)
  ctx.fillText(props.text, 0, 0)

  return {
    backgroundImage: `url(${canvas.toDataURL()})`,
    backgroundRepeat: 'repeat',
    backgroundSize: `${size}px ${size}px`,
  }
})
</script>

<style scoped>
.t-watermark {
  position: relative;
}
.t-watermark__overlay {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 1;
}
</style>
